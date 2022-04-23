using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using po.DataAccess;
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
        private readonly PoStorage poBlobStorage;

        public PoCommand(
            IServiceProvider serviceProvider,
            PoStorage poBlobStorage)
        {
            this.serviceProvider = serviceProvider;
            this.poBlobStorage = poBlobStorage;
        }

        public override SlashCommand ExpectedCommand => new()
        {
            Name = "po",
            Version = 3,
            IsGuildLevel = true,
            RequiresChannelEnablement = true
        };

        public override SlashCommandProperties BuiltCommand => new SlashCommandBuilder()
            .WithName(this.ExpectedCommand.Name)
            .WithDescription("Displays images from blob storage.")
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
            .Build();

        public override async Task HandleCommandAsync(SocketSlashCommand payload)
        {
            string operation = payload.Data.Options.First().Name;
            string category = payload.Data.Options.First().Options?.FirstOrDefault()?.Value as string;
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
                PoBlob blob = await poContext.Blobs
                                .Where(x => x.Category.StartsWith(category ?? string.Empty) && !x.Seen)
                                .OrderBy(x => Guid.NewGuid())
                                .FirstOrDefaultAsync();

                if (blob == default)
                {
                    await payload.RespondAsync($"Category prefix `{category ?? "(any)"}` has no images remaining. Try `/po reset category`");
                }
                else
                {
                    var counts = await poContext.Blobs
                                .Where(x => x.Category.StartsWith(category ?? string.Empty) && !x.Seen)
                                .GroupBy(x => x.Category)
                                .Select(g => new
                                {
                                    g.Key,
                                    CountUnseen = g.Count()
                                })
                                .ToListAsync();
                    double chance = 1.0 * counts.Single(c => c.Key == blob.Category).CountUnseen / counts.Sum(c => c.CountUnseen);

                    var builder = new EmbedBuilder()
                    {
                        Title = blob.Name,
                        Description = $"Request: `{category ?? "(any)"}` ({payload.User.Username})\nResponse category chance: {chance:P2}",
                        ImageUrl = this.poBlobStorage.GetOneDayReadOnlySasUri(blob).AbsoluteUri
                    };
                    await payload.RespondAsync(embed: builder.Build());

                    blob.Seen = true;
                    _ = await poContext.SaveChangesAsync();
                }
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

            // Default behavior is to throw up
            else
            {
                throw new NotImplementedException($"`{operation}` with category `{category ?? "(any)"}` is not complete.");
            }
        }
    }
}
