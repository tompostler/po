using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using po.DataAccess;
using po.Extensions;
using po.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace po.DiscordImpl.SlashCommands
{
    public class PoCommand : SlashCommandBase
    {
        private readonly IServiceProvider serviceProvider;
        private readonly PoStorage poStorage;
        private readonly Random random = new();

        public PoCommand(
            IServiceProvider serviceProvider,
            PoStorage poBlobStorage)
        {
            this.serviceProvider = serviceProvider;
            this.poStorage = poBlobStorage;
        }

        public override SlashCommand ExpectedCommand => new()
        {
            Name = "po",
            Version = 5,
            IsGuildLevel = true,
            RequiresChannelEnablement = true
        };

        public override SlashCommandProperties BuiltCommand => new SlashCommandBuilder()
            .WithName(this.ExpectedCommand.Name)
            .WithDescription("Displays images from blob storage.")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("random")
                .WithDescription("Schedule the display of a bunch of images.")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("count")
                    .WithDescription("The number of images to display.")
                    .WithType(ApplicationCommandOptionType.Integer)
                    .WithRequired(true)
                )
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("duration")
                    .WithDescription("The duration to display images expressed as a number suffixed with 'd', 'h', or 'm' for scale.")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(true)
                )
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("category")
                    .WithDescription("The category (or category prefix) for images to display.")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(false)
                )
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("reset")
                .WithDescription("Reset the seen statistics.")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("category")
                    .WithDescription("The category (or category prefix) of images to reset the seen status.")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(true)
                )
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("show")
                .WithDescription("Displays a single image.")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("category")
                    .WithDescription("The category (or category prefix) of image to display.")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(false)
                )
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("status")
                .WithDescription("Generates a status report of viewed images.")
                .WithType(ApplicationCommandOptionType.SubCommand)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("timer")
                .WithDescription("Schedule the display of a bunch of images.")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("interval")
                    .WithDescription("The time between displayed images expressed as a number suffixed with 'd', 'h', or 'm' for scale.")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(true)
                )
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("duration")
                    .WithDescription("The duration to display images expressed as a number suffixed with 'd', 'h', or 'm' for scale.")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(true)
                )
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("category")
                    .WithDescription("The category (or category prefix) for images to display.")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(false)
                )
            )
            .Build();

        public override async Task HandleCommandAsync(SocketSlashCommand payload)
        {
            string operation = payload.Data.Options.First().Name;
            string category = payload.Data.Options.First().Options?.FirstOrDefault(x => x.Name == "category")?.Value as string;
            using IServiceScope scope = this.serviceProvider.CreateScope();
            using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();
            SlashCommandChannel command = await poContext.SlashCommandChannels.SingleAsync(sc => sc.SlashCommandName == payload.CommandName && sc.ChannelId == payload.ChannelId);

            if (string.IsNullOrWhiteSpace(command.RegistrationData))
            {
                await payload.RespondAsync("This channel is not associated with any containers, and needs to be to be usable. Try `/po-configure associate <container-name>`.");
            }

            else if (operation == "reset")
            {
                List<string> categoriesToReset = await poContext.Blobs
                            .Where(x => x.Category.StartsWith(category ?? string.Empty) && x.Seen)
                            .GroupBy(x => x.Category)
                            .Select(g => g.Key)
                            .ToListAsync();

                int totalCountReset = 0;
                foreach (string categoryToReset in categoriesToReset)
                {
                    int countReset = await poContext.Database.ExecuteSqlRawAsync("UPDATE [Blobs] SET [Seen] = 0 WHERE [Category] = {0} AND [Seen] = 1", categoryToReset);
                    totalCountReset += countReset;
                    _ = await payload.Channel.SendMessageAsync($"Reset view status for {countReset} images in the `{categoryToReset}` category.");
                }
                await payload.RespondAsync($"Reset view status for {totalCountReset} total images across {categoriesToReset.Count} categories.");
            }

            else if (operation == "show")
            {
                await DiscordExtensions.SendSingleImageAsync(
                    this.serviceProvider,
                    this.poStorage,
                    category,
                    payload.User.Username,
                    (message) => payload.RespondAsync(message),
                    (embed) => payload.RespondAsync(embed: embed));
            }

            else if (operation == "status")
            {
                var statuses = await poContext.Blobs
                                .Where(b => b.ContainerName == command.RegistrationData)
                                .GroupBy(b => b.Category)
                                .Select(g => new
                                {
                                    g.Key,
                                    CountSeen = g.Count(b => b.Seen),
                                    CountUnseen = g.Count(b => !b.Seen)
                                })
                                .OrderBy(g => g.Key)
                                .ToListAsync();

                StringBuilder response = new();
                int catLen = Math.Max("category".Length, statuses.Max(x => x.Key.Length));
                int numLen = 8;
                _ = response.AppendLine("```");
                _ = response.AppendLine($"{"CATEGORY".PadRight(catLen)}  {"SEEN".PadLeft(numLen)}  {"UNSEEN".PadLeft(numLen)}  {"TOTAL".PadLeft(numLen)}  {"PERCENT VIEWED".PadLeft(numLen)}");
                foreach (var status in statuses)
                {
                    _ = response.Append(status.Key.PadRight(catLen));
                    _ = response.Append("  ");
                    _ = response.Append(status.CountSeen.ToString().PadLeft(numLen));
                    _ = response.Append("  ");
                    _ = response.Append(status.CountUnseen.ToString().PadLeft(numLen));
                    _ = response.Append("  ");
                    int loctot = status.CountSeen + status.CountUnseen;
                    _ = response.Append(loctot.ToString().PadLeft(numLen));
                    _ = response.Append("  ");
                    _ = response.Append((1d * status.CountSeen / loctot).ToString("P2").PadLeft(numLen));
                    _ = response.AppendLine();
                }
                _ = response.Append("TOTAL".PadRight(catLen));
                _ = response.Append("  ");
                _ = response.Append(statuses.Sum(x => x.CountSeen).ToString().PadLeft(numLen));
                _ = response.Append("  ");
                _ = response.Append(statuses.Sum(x => x.CountUnseen).ToString().PadLeft(numLen));
                _ = response.Append("  ");
                int ovetot = statuses.Sum(x => x.CountSeen + x.CountUnseen);
                _ = response.Append(ovetot.ToString().PadLeft(numLen));
                _ = response.Append("  ");
                _ = response.Append((1d * statuses.Sum(x => x.CountSeen) / ovetot).ToString("P2").PadLeft(numLen));
                _ = response.AppendLine("```");
                await payload.RespondAsync(response.ToString());
            }

            else if (operation == "random")
            {
                string errorMessage = default;

                long? countRequested = (long?)payload.Data.Options.First().Options?.FirstOrDefault(x => x.Name == "count")?.Value;
                if (!countRequested.HasValue)
                {
                    errorMessage = "`count` must be supplied.";
                }
                else if (countRequested <= 0 || countRequested > 200)
                {
                    errorMessage = $"Count must be between 0 and 200. (Is currently `{countRequested}`)";
                }

                string durationInput = payload.Data.Options.First().Options?.FirstOrDefault(x => x.Name == "duration")?.Value as string;
                if (string.IsNullOrEmpty(durationInput))
                {
                    errorMessage = "`duration` must be supplied.";
                }
                (string msg, TimeSpan parsed) duration = GetTimeSpan(durationInput);
                if (duration.msg != default)
                {
                    errorMessage = duration.msg;
                }
                else if (duration.parsed > TimeSpan.FromDays(30))
                {
                    errorMessage = $"Duration cannot last more than 30 days. (Is currently `{duration.parsed}`)";
                }
                else if (duration.parsed.TotalMinutes / countRequested < 5)
                {
                    errorMessage = "Cannot randomize more than 1 image per 5 minutes.";
                }

                int countAvailable = await poContext.Blobs.CountAsync(x => x.Category.StartsWith(category ?? string.Empty) && !x.Seen);
                if (countRequested > countAvailable * 1.1)
                {
                    errorMessage = $"Requested to schedule {countRequested}, but only {countAvailable} are available (including a 10% buffer). Request fewer scheduled images.";
                }

                if (errorMessage != default)
                {
                    await payload.RespondAsync(errorMessage);
                    return;
                }

                // Allow time to respond to the command
                await payload.DeferAsync();

                for (int i = 0; i < countRequested; i++)
                {
                    _ = poContext.ScheduledBlobs.Add(
                        new ScheduledBlob
                        {
                            Category = category,
                            ChannelId = command.ChannelId,
                            ScheduledDate = DateTimeOffset.UtcNow.AddSeconds(duration.parsed.TotalSeconds * this.random.NextDouble()),
                            Username = payload.User.Username + $", random {i + 1}/{countRequested}"
                        });
                }
                _ = await poContext.SaveChangesAsync();

                await payload.RespondAsync($"Scheduled {countRequested} images randomly over the next {duration.parsed}.");
            }

            else if (operation == "timer")
            {
                string errorMessage = default;

                string intervalInput = payload.Data.Options.First().Options?.FirstOrDefault(x => x.Name == "interval")?.Value as string;
                if (string.IsNullOrEmpty(intervalInput))
                {
                    errorMessage = "`interval` must be supplied.";
                }
                (string msg, TimeSpan parsed) interval = GetTimeSpan(intervalInput);
                if (interval.msg != default)
                {
                    errorMessage = interval.msg;
                }
                else if (interval.parsed < TimeSpan.FromSeconds(30) || interval.parsed > TimeSpan.FromDays(3))
                {
                    errorMessage = $"Interval must be between 30 seconds and 3 days. (Is currently `{interval.parsed}`)";
                }

                string durationInput = payload.Data.Options.First().Options?.FirstOrDefault(x => x.Name == "duration")?.Value as string;
                if (string.IsNullOrEmpty(durationInput))
                {
                    errorMessage = "`duration` must be supplied.";
                }
                (string msg, TimeSpan parsed) duration = GetTimeSpan(durationInput);
                if (duration.msg != default)
                {
                    errorMessage = duration.msg;
                }
                else if (duration.parsed > TimeSpan.FromDays(30))
                {
                    errorMessage = $"Duration cannot last more than 30 days. (Is currently `{duration.parsed}`)";
                }

                int countRequested = (int)(duration.parsed.TotalSeconds / interval.parsed.TotalSeconds) + 1;
                int countAvailable = await poContext.Blobs.CountAsync(x => x.Category.StartsWith(category ?? string.Empty) && !x.Seen);
                if (countRequested > countAvailable * 1.1)
                {
                    errorMessage = $"Requested to schedule {countRequested}, but only {countAvailable} are available (including a 10% buffer). Request fewer scheduled images.";
                }

                if (errorMessage != default)
                {
                    await payload.RespondAsync(errorMessage);
                    return;
                }

                // Allow time to respond to the command
                await payload.DeferAsync();

                for (int i = 0; i < countRequested; i++)
                {
                    _ = poContext.ScheduledBlobs.Add(
                        new ScheduledBlob
                        {
                            Category = category,
                            ChannelId = command.ChannelId,
                            ScheduledDate = DateTimeOffset.UtcNow.AddSeconds(interval.parsed.TotalSeconds * i),
                            Username = payload.User.Username + $", timer {i + 1}/{countRequested}"
                        });
                }
                _ = await poContext.SaveChangesAsync();

                await payload.RespondAsync($"Scheduled {countRequested} images every {interval.parsed} for the next {duration.parsed}.");
            }

            // Default behavior is to throw up
            else
            {
                throw new NotImplementedException($"`{operation}` with category `{category ?? "(any)"}` is not complete.");
            }
        }

        private static (string msg, TimeSpan parsed) GetTimeSpan(string source)
        {
            // Look for the following formats:
            //  ##d --> number of days
            //  ##h --> number of hours
            //  ##m --> number of minutes
            //  HH:MM:SS --> regular timespan parsing

            source = source.ToLower();
            double parsed;
            if (source.Contains('m'))
            {
                return double.TryParse(source.Replace("m", string.Empty), out parsed)
                    ? (null, TimeSpan.FromMinutes(parsed))
                    : ($"Found a `m`, but couldn't parse minutes from '{source}'.", default);
            }
            else if (source.Contains('h'))
            {
                return double.TryParse(source.Replace("h", string.Empty), out parsed)
                    ? (null, TimeSpan.FromHours(parsed))
                    : ($"Found a `h`, but couldn't parse hours from '{source}'.", default);
            }
            else if (source.Contains('d'))
            {
                return double.TryParse(source.Replace("d", string.Empty), out parsed)
                    ? (null, TimeSpan.FromDays(parsed))
                    : ($"Found a `d`, but couldn't parse days from '{source}'.", default);
            }
            else
            {
                return TimeSpan.TryParse(source, out TimeSpan parsedt)
                    ? (null, parsedt)
                    : ($"Couldn't parse a `TimeSpan` from '{source}'.", default);
            }
        }


    }
}
