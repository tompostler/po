using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using po.DataAccess;
using po.Extensions;
using po.Models;
using po.Utilities;
using System.Text;

namespace po.DiscordImpl.SlashCommands
{
    public class PoCommand : SlashCommandBase
    {
        private readonly IServiceProvider serviceProvider;
        private readonly PoStorage poStorage;
        private readonly Delays delays;
        private readonly ILogger<PoCommand> logger;

        private readonly Random random = new();

        public PoCommand(
            IServiceProvider serviceProvider,
            PoStorage poBlobStorage,
            Delays delays,
            ILogger<PoCommand> logger)
        {
            this.serviceProvider = serviceProvider;
            this.poStorage = poBlobStorage;
            this.delays = delays;
            this.logger = logger;
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
            using IServiceScope scope = this.serviceProvider.CreateScope();
            using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();
            SlashCommandChannel command = await poContext.SlashCommandChannels.SingleOrDefaultAsync(sc => sc.SlashCommandName == payload.CommandName && sc.ChannelId == payload.ChannelId);

            if (string.IsNullOrWhiteSpace(command?.RegistrationData))
            {
                await payload.RespondAsync("This channel is not associated with any containers, and needs to be to be usable. Try `/po-configure associate <container-name>`.");
            }

            string operation = payload.Data.Options.First().Name;
            string category = payload.Data.Options.First().Options?.FirstOrDefault(x => x.Name == "category")?.Value as string;
            switch (operation)
            {
                case "random":
                    await this.HandleRandomAsync(payload, command, category, poContext);
                    this.delays.ScheduledBlob.CancelDelay();
                    break;

                case "reset":
                    await HandleResetAsync(payload, command, category, poContext);
                    break;

                case "show":
                    await DiscordExtensions.SendSingleImageAsync(
                        this.serviceProvider,
                        this.poStorage,
                        command.RegistrationData,
                        category,
                        payload.User.Username,
                        (message) => payload.RespondAsync(message),
                        (embed) => payload.RespondAsync(embed: embed));
                    break;

                case "status":
                    await this.HandleStatusAsync(payload, command, poContext);
                    break;

                case "timer":
                    await HandleTimerAsync(payload, command, category, poContext);
                    this.delays.ScheduledBlob.CancelDelay();
                    break;

                // Default behavior is to throw up
                default:
                    throw new NotImplementedException($"`{operation}` with category `{category ?? "(any)"}` is not complete.");
            }
        }

        public async Task HandleNaiveCommandAsync(DiscordSocketClient discordClient, IMessage imessage)
        {
            // [0]: Slash command, so basically ignore
            // [1]: Subcommand
            // [2+]: Arguments (implicit or explicit)
            string[] contentChunks = imessage.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            contentChunks[0] = contentChunks[0].TrimStart('/').ToLower();

            using IServiceScope scope = this.serviceProvider.CreateScope();
            using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();
            SlashCommandChannel command = await poContext.SlashCommandChannels.SingleOrDefaultAsync(sc => sc.SlashCommandName == contentChunks[0] && sc.ChannelId == imessage.Channel.Id);

            if (string.IsNullOrWhiteSpace(command?.RegistrationData))
            {
                await discordClient.SendTextMessageAsync(
                    imessage.Channel.Id,
                    "This channel is not associated with any containers, and needs to be to be usable. Try `/po-configure associate <container-name>`.",
                    this.logger,
                    CancellationToken.None);
                return;
            }

            if (contentChunks.Length == 1)
            {
                await discordClient.SendTextMessageAsync(
                    imessage.Channel.Id,
                    "/po naive invocation requires a subcommand to do something, such as `/po show`.",
                    this.logger,
                    CancellationToken.None);
                return;
            }

            string category = contentChunks.Length > 2 ? contentChunks[2] : string.Empty;
            switch (contentChunks[1])
            {
                case "show":
                    await DiscordExtensions.SendSingleImageAsync(
                        this.serviceProvider,
                        this.poStorage,
                        command.RegistrationData,
                        category,
                        imessage.Author.Username,
                    (msg) => discordClient.SendTextMessageAsync(imessage.Channel.Id, msg, this.logger, CancellationToken.None),
                    (embed) => discordClient.SendEmbedMessageAsync(imessage.Channel.Id, embed, this.logger, CancellationToken.None));
                    break;

                default:
                    await discordClient.SendTextMessageAsync(
                        imessage.Channel.Id,
                        "/po naive invocation requires a known subcommand to do something, such as `/po show`.",
                        this.logger,
                        CancellationToken.None);
                    break;
            }
        }

        private async Task HandleRandomAsync(SocketSlashCommand payload, SlashCommandChannel command, string category, PoContext poContext)
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

            // Create the random intervals (and sort them for sequential counting)
            var delays = new TimeSpan[countRequested.Value + 1];
            delays[0] = TimeSpan.Zero;
            for (int i = 1; i < delays.Length; i++)
            {
                delays[i] = TimeSpan.FromMinutes(duration.parsed.TotalMinutes * this.random.NextDouble());
            }
            Array.Sort(delays);

            // Actually schedule the delays
            for (int i = 0; i < delays.Length; i++)
            {
                _ = poContext.ScheduledBlobs.Add(
                    new ScheduledBlob
                    {
                        ContainerName = command.RegistrationData,
                        ChannelId = command.ChannelId,
                        Category = category,
                        Username = payload.User.Username + $", random {i + 1}/{delays.Length}",
                        ScheduledDate = DateTimeOffset.UtcNow.Add(delays[i]),
                    });
            }
            _ = await poContext.SaveChangesAsync();

            _ = await payload.FollowupAsync($"Scheduled {countRequested} images randomly over the next {duration.parsed}.");
        }

        private static async Task HandleResetAsync(SocketSlashCommand payload, SlashCommandChannel command, string category, PoContext poContext)
        {
            await payload.DeferAsync();

            List<string> categoriesToReset = await poContext.Blobs
                        .Where(x => x.ContainerName == command.RegistrationData && x.Category.StartsWith(category ?? string.Empty) && x.Seen)
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

            _ = await payload.FollowupAsync($"Reset view status for {totalCountReset} total images across {categoriesToReset.Count} categories.");
        }

        private async Task HandleStatusAsync(SocketSlashCommand payload, SlashCommandChannel command, PoContext poContext)
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

            if (statuses.Count == 0)
            {
                await payload.RespondAsync("No images known.");
                return;
            }

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

            string responseString = response.ToString();
            this.logger.LogInformation($"Response length is: {responseString.Length}");
            if (responseString.Length > 2000)
            {
                var fileStream = new MemoryStream(Encoding.UTF8.GetBytes(responseString.Trim().Trim('`')));
                await payload.RespondWithFileAsync(fileStream, "po-status.txt");
            }
            else
            {
                await payload.RespondAsync(responseString);
            }
        }

        private static async Task HandleTimerAsync(SocketSlashCommand payload, SlashCommandChannel command, string category, PoContext poContext)
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
                        ContainerName = command.RegistrationData,
                        ChannelId = command.ChannelId,
                        Category = category,
                        Username = payload.User.Username + $", timer {i + 1}/{countRequested}",
                        ScheduledDate = DateTimeOffset.UtcNow.AddSeconds(interval.parsed.TotalSeconds * i),
                    });
            }
            _ = await poContext.SaveChangesAsync();

            _ = await payload.FollowupAsync($"Scheduled {countRequested} images every {interval.parsed} for the next {duration.parsed}.");
        }
    }
}
