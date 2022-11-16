using Discord;
using Discord.WebSocket;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using po.DataAccess;
using po.Extensions;
using po.Models;
using po.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
            var telemObj = new
            {
                channelId = message.Channel.Id,
                messageId = message.Id,
                message.Content,
                embedCount = message.Embeds.Count,
                componentCount = message.Components.Count,
            };
            this.logger.LogInformation($"Message received: {telemObj.ToJsonString()}");

            // Bail out if it's a System Message.
            if (message is not SocketUserMessage userMessage)
            {
                return;
            }

            // We don't want the bot to respond to itself or other bots.
            if (userMessage.Author.Id == this.discordClient.CurrentUser.Id || userMessage.Author.IsBot)
            {
                return;
            }

            // If we want to actually do something based on messages received, that would go here

            // If it's a naive po command (e.g. /po show [category]), then handle it
            if (message.Content?.StartsWith("/po show") == true)
            {
                string category = message.Content.Substring("/po show".Length);

                using IServiceScope scope = this.serviceProvider.CreateScope();
                using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();
                SlashCommandChannel command = await poContext.SlashCommandChannels.SingleOrDefaultAsync(sc => sc.SlashCommandName == "po" && sc.ChannelId == message.Channel.Id);

                await DiscordExtensions.SendSingleImageAsync(
                    this.serviceProvider,
                    this.poBlobStorage,
                    command.RegistrationData,
                    category,
                    message.Author.Username,
                    (msg) => this.discordClient.SendTextMessageAsync(message.Channel.Id, msg, this.logger, CancellationToken.None),
                    (embed) => this.discordClient.SendEmbedMessageAsync(message.Channel.Id, embed, this.logger, CancellationToken.None));
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
                    }
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, "Could not register a command.");
                }
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
