using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using po.DataAccess;
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
        private readonly Sentinals sentinals;
        private readonly Options.Discord options;
        private readonly ILogger<BotService> logger;

        private DiscordSocketClient discordClient;

        public BotService(
            IServiceProvider serviceProvider,
            Sentinals sentinals,
            IOptions<Options.Discord> options,
            ILogger<BotService> logger)
        {
            this.serviceProvider = serviceProvider;
            this.sentinals = sentinals;
            this.options = options.Value;
            this.logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            this.discordClient = new DiscordSocketClient();
            this.discordClient.Log += this.DiscordClientLog;
            this.discordClient.MessageReceived += this.DiscordMessageReceived;

            string botToken = this.options.BotToken;
            this.logger.LogInformation($"Token is {botToken?.Length.ToString() ?? "<null>"} characters.");

            await this.discordClient.LoginAsync(Discord.TokenType.Bot, botToken);
            await this.discordClient.StartAsync();

            this.discordClient.Ready += () => this.DiscordClient_Ready(cancellationToken);
            this.discordClient.SlashCommandExecuted += this.DiscordClient_SlashCommandExecuted;
        }

        private async Task DiscordClient_SlashCommandExecuted(SocketSlashCommand arg)
        {
            Models.SlashCommand command;
            using (IServiceScope scope = this.serviceProvider.CreateScope())
            using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
            {
                command = await poContext.SlashCommands.FirstOrDefaultAsync(sc => sc.Name == arg.CommandName);
            }
            if (command == default)
            {
                await arg.RespondAsync($"`{arg.CommandName}` is not registered for handling.");
            }

            switch (arg.CommandName)
            {
                default:
                    await arg.RespondAsync($"Command `{arg.CommandName}` ({arg.CommandId}) is registered but not mapped to handling.");
                    break;
            }
        }

        private Task DiscordClientLog(Discord.LogMessage arg)
        {
            LogLevel logLevel = arg.Severity switch
            {
                Discord.LogSeverity.Critical => LogLevel.Critical,
                Discord.LogSeverity.Error => LogLevel.Error,
                Discord.LogSeverity.Warning => LogLevel.Warning,
                Discord.LogSeverity.Info => LogLevel.Information,
                Discord.LogSeverity.Verbose => LogLevel.Trace,
                Discord.LogSeverity.Debug => LogLevel.Debug,
                _ => LogLevel.Information
            };

            this.logger.Log(logLevel, arg.ToString(prependTimestamp: false));

            return Task.CompletedTask;
        }

        private Task DiscordMessageReceived(SocketMessage message)
        {
            this.logger.LogInformation($"Message received: {message}");

            // Bail out if it's a System Message.
            if (message is not SocketUserMessage userMessage)
            {
                return Task.CompletedTask;
            }

            // We don't want the bot to respond to itself or other bots.
            if (userMessage.Author.Id == this.discordClient.CurrentUser.Id || userMessage.Author.IsBot)
            {
                return Task.CompletedTask;
            }

            // If we want to actually do something based on messages received, that would go here
            return Task.CompletedTask;
        }

        private async Task DiscordClient_Ready(CancellationToken cancellationToken)
        {
            await this.EnsureDataModelIsUpToDateAsync(cancellationToken);
            await this.RegisterSlashCommandsAsync(cancellationToken);
            await this.TrySendNotificationTextMessageAsync($"I have been restarted on {Environment.MachineName}. v{typeof(BotService).Assembly.GetName().Version.ToString(3)}");

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
                        if (allTextChannels.Any(tc => slashCommandChannel.GuildId == tc.Guild.Id && slashCommandChannel.ChannelId == tc.Id))
                        {
                            channelsToKeep.Add(slashCommandChannel);
                        }
                        else
                        {
                            await this.TrySendNotificationTextMessageAsync($"Removed {slashCommandChannel}");
                        }
                    }
                    slashCommand.EnabledChannels = channelsToKeep;
                }
                _ = await poContext.SaveChangesAsync(cancellationToken);
            }
        }

        private async Task RegisterSlashCommandsAsync(CancellationToken cancellationToken)
        {
            try
            {
                SocketGuild primaryGuild = this.discordClient.GetGuild(this.options.BotPrimaryGuildId);

                SlashCommandBuilder builder = new()
                {
                    Name = "echo",
                    Description = "Returns what you said back to you."
                };
                Models.SlashCommand command;
                using (IServiceScope scope = this.serviceProvider.CreateScope())
                using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
                {
                    command = await poContext.SlashCommands.FirstOrDefaultAsync(sc => sc.Name == builder.Name, cancellationToken);
                    command ??= new()
                    {
                        Name = builder.Name,
                        IsGuildLevel = true
                    };
                    if (!command.SuccessfullyRegistered.HasValue)
                    {
                        SocketApplicationCommand response = command.IsGuildLevel
                            ? await primaryGuild.CreateApplicationCommandAsync(builder.Build())
                            : await this.discordClient.CreateGlobalApplicationCommandAsync(builder.Build());
                        this.logger.LogInformation($"Registered command {response.Name} ({response.Id}). IsGuildLevel={command.IsGuildLevel}");

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

        private async Task TrySendNotificationTextMessageAsync(string message)
        {
            this.logger.LogInformation($"Attempting to send notification message to {this.options.BotPrimaryGuildId}/{this.options.BotNotificationChannelId}");
            try
            {
                _ = await this.discordClient.GetGuild(this.options.BotPrimaryGuildId).GetTextChannel(this.options.BotNotificationChannelId).SendMessageAsync(message);
            }
            catch (Exception ex)
            {
                this.logger.LogError($"Could not send notification message: {ex}");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await this.discordClient.LogoutAsync();
            await this.discordClient.StopAsync();
        }
    }
}
