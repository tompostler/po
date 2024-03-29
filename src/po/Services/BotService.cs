﻿using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using po.DataAccess;
using po.Extensions;
using po.Utilities;

namespace po.Services
{
    public sealed class BotService : IHostedService
    {
        private readonly IServiceProvider serviceProvider;
        private readonly PoStorage poBlobStorage;
        private readonly Sentinals sentinals;
        private readonly Dictionary<string, DiscordImpl.SlashCommands.SlashCommandBase> slashCommands;
        private readonly Options.Discord options;
        private readonly ILogger<BotService> logger;
        private readonly TelemetryClient telemetryClient;

        private DiscordSocketClient discordClient;

        public BotService(
            IServiceProvider serviceProvider,
            PoStorage poBlobStorage,
            Sentinals sentinals,
            IEnumerable<DiscordImpl.SlashCommands.SlashCommandBase> slashCommands,
            IOptions<Options.Discord> options,
            ILogger<BotService> logger,
            TelemetryClient telemetryClient)
        {
            this.serviceProvider = serviceProvider;
            this.poBlobStorage = poBlobStorage;
            this.sentinals = sentinals;
            this.slashCommands = slashCommands.ToDictionary(x => x.ExpectedCommand.Name);
            this.options = options.Value;
            this.logger = logger;
            this.telemetryClient = telemetryClient;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            this.discordClient = new DiscordSocketClient();
            this.discordClient.Log += this.DiscordClient_Log;
            this.discordClient.MessageReceived += this.DiscordClient_MessageReceived;

            string botToken = this.options.BotToken;
            this.logger.LogInformation($"Token is {botToken?.Length.ToString() ?? "<null>"} characters.");

            await this.discordClient.LoginAsync(TokenType.Bot, botToken);
            await this.discordClient.StartAsync();

            this.discordClient.Ready += () => this.DiscordClient_Ready(cancellationToken);
            this.discordClient.SlashCommandExecuted += this.DiscordClient_SlashCommandExecuted;
        }

        private Task DiscordClient_Log(LogMessage arg)
        {
            LogLevel logLevel = arg.Severity switch
            {
                LogSeverity.Critical => LogLevel.Critical,
                LogSeverity.Error => LogLevel.Error,
                LogSeverity.Warning => LogLevel.Warning,
                LogSeverity.Info => LogLevel.Information,
                LogSeverity.Verbose => LogLevel.Trace,
                LogSeverity.Debug => LogLevel.Debug,
                _ => LogLevel.Information
            };

            this.logger.Log(logLevel, arg.ToString(prependTimestamp: false));

            return Task.CompletedTask;
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

        private async Task DiscordClient_Ready(CancellationToken cancellationToken)
        {
            using IOperationHolder<RequestTelemetry> op = this.telemetryClient.StartOperation<RequestTelemetry>($"{this.GetType().FullName}.{nameof(DiscordClient_Ready)}");

            await this.EnsureDataModelIsUpToDateAsync(cancellationToken);
            await this.RegisterSlashCommandsAsync(cancellationToken);
            this.sentinals.DiscordClient.SignalCompletion(this.discordClient);

            this.discordClient.Ready -= () => this.DiscordClient_Ready(cancellationToken);
        }

        private async Task EnsureDataModelIsUpToDateAsync(CancellationToken cancellationToken)
        {
            await this.sentinals.DBMigration.WaitForCompletionAsync(cancellationToken);
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
                            await this.discordClient.TrySendNotificationTextMessageOrFileAsync(this.options, $"Removed {slashCommandChannel}", this.logger, cancellationToken);
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

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await this.discordClient.LogoutAsync();
            await this.discordClient.StopAsync();
        }
    }
}
