using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using po.DataAccess;
using po.Utilities;
using System;
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

            this.discordClient.Ready += () => this.DiscordClientReady(cancellationToken);
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

            return Task.CompletedTask;
        }

        private async Task DiscordClientReady(CancellationToken cancellationToken)
        {
            await this.EnsureDataModelIsUpToDateAsync(cancellationToken);
            await this.TrySendStartupMessageAsync();

            this.discordClient.Ready -= () => this.DiscordClientReady(cancellationToken);
        }

        private async Task EnsureDataModelIsUpToDateAsync(CancellationToken cancellationToken)
        {
            await this.sentinals.DBMigration.WaitForCompletionAsync(cancellationToken);
            this.logger.LogInformation("Migration completion signal received. Starting data sync.");

            using IServiceScope scope = this.serviceProvider.CreateScope();
            using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();
        }

        private async Task TrySendStartupMessageAsync()
        {
            this.logger.LogInformation($"Attempting to send startup message to {this.options.BotPrimaryGuildId}/{this.options.BotNotificationChannelId}");
            try
            {
                SocketGuild primaryGuild = this.discordClient.GetGuild(this.options.BotPrimaryGuildId);
                SocketTextChannel notificationTextChannel = primaryGuild.GetTextChannel(this.options.BotNotificationChannelId);
                _ = await notificationTextChannel.SendMessageAsync($"I have been restarted. v{typeof(BotService).Assembly.GetName().Version.ToString(3)}");
            }
            catch (Exception ex)
            {
                this.logger.LogError($"Could not send startup message: {ex}");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await this.discordClient.LogoutAsync();
            await this.discordClient.StopAsync();
        }
    }
}
