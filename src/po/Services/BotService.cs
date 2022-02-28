using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading;
using System.Threading.Tasks;

namespace po.Services
{
    public sealed class BotService : IHostedService
    {
        private readonly IOptions<Options.Discord> options;
        private readonly ILogger<BotService> logger;

        private DiscordSocketClient discordClient;

        public BotService(IOptions<Options.Discord> options, ILogger<BotService> logger)
        {
            this.options = options;
            this.logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            this.discordClient = new DiscordSocketClient();
            this.discordClient.Log += this.DiscordClientLog;

            string botToken = this.options.Value.BotToken;
            this.logger.LogInformation($"Token is {botToken?.Length.ToString() ?? "<null>"} characters.");

            await this.discordClient.LoginAsync(Discord.TokenType.Bot, botToken);
            await this.discordClient.StartAsync();
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

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await this.discordClient.StopAsync();
            await this.discordClient.LogoutAsync();
        }
    }
}
