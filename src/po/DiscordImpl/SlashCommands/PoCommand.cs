using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using po.DataAccess;
using po.Extensions;
using po.Models;
using po.Services.Po;
using System.Text;

namespace po.DiscordImpl.SlashCommands
{
    public class PoCommand : SlashCommandBase
    {
        private readonly IServiceProvider serviceProvider;
        private readonly PoImageService poImageService;
        private readonly PoScheduler poScheduler;
        private readonly PoStatusReport poStatusReport;
        private readonly ILogger<PoCommand> logger;

        public PoCommand(
            IServiceProvider serviceProvider,
            PoImageService poImageService,
            PoScheduler poScheduler,
            PoStatusReport poStatusReport,
            ILogger<PoCommand> logger)
        {
            this.serviceProvider = serviceProvider;
            this.poImageService = poImageService;
            this.poScheduler = poScheduler;
            this.poStatusReport = poStatusReport;
            this.logger = logger;
        }

        public override SlashCommand ExpectedCommand => new()
        {
            Name = "po",
            Version = 6,
            IsGuildLevel = true,
            RequiresChannelEnablement = true
        };

        public override SlashCommandProperties BuiltCommand => new SlashCommandBuilder()
            .WithName(this.ExpectedCommand.Name)
            .WithDescription("Displays images from blob storage.")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("clear")
                .WithDescription("Clear images from scheduled queue.")
                .WithType(ApplicationCommandOptionType.SubCommand)
            )
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
            await payload.DeferAsync();

            using IServiceScope scope = this.serviceProvider.CreateScope();
            using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();
            SlashCommandChannel command = await poContext.SlashCommandChannels.SingleOrDefaultAsync(sc => sc.SlashCommandName == payload.CommandName && sc.ChannelId == payload.ChannelId);

            if (string.IsNullOrWhiteSpace(command?.RegistrationData))
            {
                _ = await payload.FollowupAsync("This channel is not associated with any containers, and needs to be to be usable. Try `/po-configure associate <container-name>`.");
                return;
            }

            string operation = payload.Data.Options.First().Name;
            string category = payload.Data.Options.First().Options?.FirstOrDefault(x => x.Name == "category")?.Value as string;
            switch (operation)
            {
                case "clear":
                    _ = await payload.FollowupAsync(await this.poScheduler.ClearAsync(command.RegistrationData, command.ChannelId));
                    break;

                case "random":
                    _ = await payload.FollowupAsync(await this.poScheduler.ScheduleRandomAsync(
                        command.RegistrationData,
                        command.ChannelId,
                        category,
                        payload.User.Username,
                        (long?)payload.Data.Options.First().Options?.FirstOrDefault(x => x.Name == "count")?.Value,
                        payload.Data.Options.First().Options?.FirstOrDefault(x => x.Name == "duration")?.Value as string));
                    break;

                case "reset":
                    _ = await payload.FollowupAsync(await this.poImageService.ResetSeenAsync(
                        command.RegistrationData,
                        category,
                        (message) => payload.Channel.SendMessageAsync(message)));
                    break;

                case "show":
                    await DiscordExtensions.SendSingleImageAsync(
                        this.poImageService,
                        command.RegistrationData,
                        category,
                        payload.User.Username,
                        (message) => payload.FollowupAsync(message),
                        (embed) => payload.FollowupAsync(embed: embed));
                    break;

                case "status":
                    await this.HandleStatusAsync(payload, command);
                    break;

                case "timer":
                    _ = await payload.FollowupAsync(await this.poScheduler.ScheduleTimerAsync(
                        command.RegistrationData,
                        command.ChannelId,
                        category,
                        payload.User.Username,
                        payload.Data.Options.First().Options?.FirstOrDefault(x => x.Name == "interval")?.Value as string,
                        payload.Data.Options.First().Options?.FirstOrDefault(x => x.Name == "duration")?.Value as string));
                    break;

                // Default behavior is to throw up
                default:
                    throw new NotImplementedException($"`{operation}` with category `{category ?? "(any)"}` is not complete.");
            }
        }

        /// <summary>
        /// A method to handle a command that's actually just text.
        /// Useful since the discord mobile app is not the most intuitive with slash commands.
        /// </summary>
        public async Task HandleCommandAsync(DiscordSocketClient discordClient, IMessage imessage)
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
                        this.poImageService,
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

        private async Task HandleStatusAsync(SocketSlashCommand payload, SlashCommandChannel command)
        {
            string responseString = await this.poStatusReport.BuildAsync(command.RegistrationData);
            this.logger.LogInformation($"Response length is: {responseString.Length}");
            if (responseString.Length > 2000)
            {
                var fileStream = new MemoryStream(Encoding.UTF8.GetBytes(responseString.Trim().Trim('`')));
                _ = await payload.FollowupWithFileAsync(fileStream, "po-status.txt");
            }
            else
            {
                _ = await payload.FollowupAsync(responseString);
            }
        }
    }
}
