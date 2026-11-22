using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using po.Chat;
using po.DataAccess;
using po.Extensions;
using po.Platforms.Discord;
using po.Utilities;

namespace po.Services
{
    /// <summary>
    /// Handles Discord slash commands and the <c>/po</c> text fallback, and registers the commands once <see cref="DiscordPlatform"/> is ready.
    /// </summary>
    public sealed class BotService : BackgroundService
    {
        private readonly IServiceProvider serviceProvider;
        private readonly Sentinels sentinels;
        private readonly DiscordPlatform discordPlatform;
        private readonly ChatRouter chatRouter;
        private readonly Dictionary<string, DiscordImpl.SlashCommands.SlashCommandBase> slashCommands;
        private readonly Options.Discord options;
        private readonly ILogger<BotService> logger;
        private readonly TelemetryClient telemetryClient;

        private readonly DiscordSocketClient discordClient;

        public BotService(
            IServiceProvider serviceProvider,
            Sentinels sentinels,
            DiscordPlatform discordPlatform,
            ChatRouter chatRouter,
            IEnumerable<DiscordImpl.SlashCommands.SlashCommandBase> slashCommands,
            IOptions<Options.Discord> options,
            ILogger<BotService> logger,
            TelemetryClient telemetryClient)
        {
            this.serviceProvider = serviceProvider;
            this.sentinels = sentinels;
            this.discordPlatform = discordPlatform;
            this.chatRouter = chatRouter;
            this.discordClient = discordPlatform.Client;
            this.slashCommands = slashCommands.ToDictionary(x => x.ExpectedCommand.Name);
            this.options = options.Value;
            this.logger = logger;
            this.telemetryClient = telemetryClient;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            this.discordClient.MessageReceived += this.DiscordClient_MessageReceived;
            this.discordClient.SlashCommandExecuted += this.DiscordClient_SlashCommandExecuted;

            // This runs once, after the first Ready, rather than in a Ready handler.
            // A Ready handler blocks the gateway task, and it fires again on every reconnect.
            await this.discordPlatform.WaitUntilReadyAsync(stoppingToken);

            using IOperationHolder<RequestTelemetry> op = this.telemetryClient.StartOperation<RequestTelemetry>($"{this.GetType().FullName}.{nameof(ExecuteAsync)}");
            try
            {
                await this.EnsureDataModelIsUpToDateAsync(stoppingToken);
                await this.RegisterSlashCommandsAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // An unhandled exception here would stop the host
                this.logger.LogError(ex, "Could not prepare slash commands.");
            }
        }

        private async Task DiscordClient_MessageReceived(SocketMessage message)
        {
            using IOperationHolder<RequestTelemetry> op = this.telemetryClient.StartOperation<RequestTelemetry>($"{this.GetType().FullName}.{nameof(DiscordClient_MessageReceived)}");
            
            IMessage imessage = message;
            bool refetchAttempted = false;
            if (string.IsNullOrEmpty(imessage.Content) && imessage.Embeds?.Any() == false)
            {
                refetchAttempted = true;
                var channel = await this.discordClient.GetChannelAsync(imessage.Channel.Id) as IMessageChannel;
                if (channel != default)
                {
                    imessage = await channel.GetMessageAsync(imessage.Id);
                }
            }
            var trimmed = new
            {
                channelId = imessage.Channel.Id,
                id = imessage.Id,
                type = imessage.Type,
                netType = imessage.GetType().Name,
                content = !string.IsNullOrEmpty(imessage.Content) ? imessage.Content : (imessage.Embeds?.Any() == true ? imessage.Embeds.First().ToString() : string.Empty),
                embedCount = imessage.Embeds?.Count,
                refetchAttempted
            };
            this.logger.LogInformation($"Message received: {trimmed.ToJsonString()}");

            // Bail out if it's not a user-initiated message (socket or rest variant)
            if (imessage is not IUserMessage userMessage)
            {
                return;
            }

            // We don't want the bot to respond to itself or other bots
            if (userMessage.Author.Id == this.discordClient.CurrentUser.Id || userMessage.Author.IsBot)
            {
                return;
            }

            // The sku of this app service, combined with the clunkiness of the slash commands in discord means
            // that it's worth it to just also parse a message manually and respond

            // Try routing/parsing /po
            if (trimmed.content.StartsWith("/po"))
            {
                await (this.slashCommands["po"] as DiscordImpl.SlashCommands.PoCommand)?.HandleCommandAsync(this.discordClient, imessage);
            }
        }

        private async Task EnsureDataModelIsUpToDateAsync(CancellationToken cancellationToken)
        {
            await this.sentinels.DBMigration.WaitForCompletionAsync(cancellationToken);
            this.logger.LogInformation("Migration completion signal received. Starting data sync.");

            IEnumerable<SocketTextChannel> allTextChannels = this.discordClient.Guilds.SelectMany(g => g.TextChannels);

            // Remove slash command registrations for any channels that no longer exist
            using (IServiceScope scope = this.serviceProvider.CreateScope())
            using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
            {
                List<Models.SlashCommand> allSlashCommands = await poContext.SlashCommands.Include(sc => sc.EnabledChannels).ToListAsync(cancellationToken);
                foreach (Models.SlashCommand slashCommand in allSlashCommands)
                {
                    List<Models.SlashCommandChannel> channelsToKeep = new();
                    foreach (Models.SlashCommandChannel slashCommandChannel in slashCommand.EnabledChannels ?? Enumerable.Empty<Models.SlashCommandChannel>())
                    {
                        if (allTextChannels.Any(tc => slashCommandChannel.ChannelId == tc.Id))
                        {
                            channelsToKeep.Add(slashCommandChannel);
                        }
                        else
                        {
                            await this.chatRouter.SendNotificationTextAsync($"Removed {slashCommandChannel}", cancellationToken);
                        }
                    }
                    slashCommand.EnabledChannels = channelsToKeep;
                }
                _ = await poContext.SaveChangesAsync(cancellationToken);
            }
        }

        /// <remarks>
        /// Note: Does not currently know how to de-register a command or change from guild to global level.
        /// </remarks>
        private async Task RegisterSlashCommandsAsync(CancellationToken cancellationToken)
        {
            SocketGuild primaryGuild = this.discordClient.GetGuild(this.options.BotPrimaryGuildId);

            HashSet<string> knownCommandNames = new();

            foreach (DiscordImpl.SlashCommands.SlashCommandBase slashCommand in this.slashCommands.Values)
            {
                try
                {
                    using (IServiceScope scope = this.serviceProvider.CreateScope())
                    using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
                    {
                        Models.SlashCommand command = await poContext.SlashCommands.FirstOrDefaultAsync(sc => sc.Name == slashCommand.ExpectedCommand.Name, cancellationToken);
                        if (command == default)
                        {
                            command = slashCommand.ExpectedCommand;
                            _ = poContext.SlashCommands.Add(command);
                        }
                        if (!command.SuccessfullyRegistered.HasValue || command.Version != slashCommand.ExpectedCommand.Version)
                        {
                            this.logger.LogInformation($"Registering command {slashCommand.ExpectedCommand.Name}");
                            SocketApplicationCommand response = command.IsGuildLevel
                                ? await primaryGuild.CreateApplicationCommandAsync(slashCommand.BuiltCommand, cancellationToken.ToRO())
                                : await this.discordClient.CreateGlobalApplicationCommandAsync(slashCommand.BuiltCommand, cancellationToken.ToRO());
                            this.logger.LogInformation($"Registered command {response.Name} ({response.Id}). IsGuildLevel={command.IsGuildLevel}");

                            command.UpdateBasics(slashCommand.ExpectedCommand);
                            command.Id = response.Id;
                            command.SuccessfullyRegistered = DateTimeOffset.UtcNow;
                            _ = await poContext.SaveChangesAsync(cancellationToken);
                        }
                        knownCommandNames.Add(command.Name);
                    }
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, "Could not register a command.");
                }
            }

            // Remove unknown global commands
            IReadOnlyCollection<SocketApplicationCommand> knownGlobalCommands = await this.discordClient.GetGlobalApplicationCommandsAsync(options: cancellationToken.ToRO());
            foreach (SocketApplicationCommand unknownGlobalCommand in knownGlobalCommands.Where(x => !knownCommandNames.Contains(x.Name)))
            {
                this.logger.LogInformation($"Deleting global command [{unknownGlobalCommand.Name}]");
                await unknownGlobalCommand.DeleteAsync(cancellationToken.ToRO());
            }

            // Remove unknown guild commands
            IReadOnlyCollection<SocketApplicationCommand> knownGuildCommands = await primaryGuild.GetApplicationCommandsAsync(options: cancellationToken.ToRO());
            foreach (SocketApplicationCommand unknownGuildCommand in knownGuildCommands.Where(x => !knownCommandNames.Contains(x.Name)))
            {
                this.logger.LogInformation($"Deleting guild command [{unknownGuildCommand.Name}]");
                await unknownGuildCommand.DeleteAsync(cancellationToken.ToRO());
            }
        }

        private async Task DiscordClient_SlashCommandExecuted(SocketSlashCommand payload)
        {
            using IOperationHolder<RequestTelemetry> op = this.telemetryClient.StartOperation<RequestTelemetry>($"{this.GetType().FullName}.{nameof(DiscordClient_SlashCommandExecuted)}");

            try
            {
                Models.SlashCommand command;
                using (IServiceScope scope = this.serviceProvider.CreateScope())
                using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
                {
                    command = await poContext.SlashCommands.Include(x => x.EnabledChannels).FirstOrDefaultAsync(sc => sc.Name == payload.CommandName);
                }
                if (command == default)
                {
                    await payload.RespondAsync($"`{payload.CommandName}` is not registered for handling.");
                }
                else if (command.RequiresChannelEnablement && !command.EnabledChannels.Any(x => x.ChannelId == payload.Channel.Id))
                {
                    await payload.RespondAsync($"`{payload.CommandName}` is not enabled for the current channel.");
                }

                if (this.slashCommands.ContainsKey(payload.CommandName))
                {
                    await this.slashCommands[payload.CommandName].HandleCommandAsync(payload);
                }
                else
                {
                    await payload.RespondAsync($"Command `{payload.CommandName}` ({payload.CommandId}) is registered but not mapped to handling.");
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Could not handle slash command.");
                try
                {
                    if (!payload.HasResponded)
                    {
                        // Trim the exception to the 2000 char limit for discord messages
                        string exString = ex.ToString();
                        if (exString.Length > 2000)
                        {
                            exString = exString.Substring(0, 2000);
                        }

                        await payload.RespondAsync($"Caught an exception: {exString}");
                    }
                }
                catch (Exception nestedEx)
                {
                    this.logger.LogError(nestedEx, "Could not respond again after initial exception.");
                }
            }
        }
    }
}
