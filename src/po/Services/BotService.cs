using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using po.DataAccess;
using System.Threading;
using System.Threading.Tasks;

namespace po.Services
{
    public sealed class BotService : IHostedService
    {
        private readonly MigrationInitCompletionSignal signal;
        private readonly Options.Discord options;
        private readonly ILogger<BotService> logger;

        private DiscordSocketClient discordClient;

        public BotService(
            MigrationInitCompletionSignal signal,
            IOptions<Options.Discord> options,
            ILogger<BotService> logger)
        {
            this.signal = signal;
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

            _ = this.EnsureStoredDataMatchesAsync(cancellationToken);
        }

        private Task DiscordClientLog(Discord.LogMessage arg)
        {
            var logLevel = arg.Severity switch
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

        private async Task EnsureStoredDataMatchesAsync(CancellationToken cancellationToken)
        {
            await this.signal.WaitForCompletionAsync(cancellationToken);
            this.logger.LogInformation("Migration completion signal received. Starting data sync.");

            SocketGuild primaryGuild = this.discordClient.GetGuild(this.options.BotPrimaryGuildId);
            SocketTextChannel notificationTextChannel = primaryGuild.GetTextChannel(this.options.BotNotificationChannelId);
            _ = await notificationTextChannel.SendMessageAsync("I live!");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await this.discordClient.StopAsync();
            await this.discordClient.LogoutAsync();
        }
    }
}
