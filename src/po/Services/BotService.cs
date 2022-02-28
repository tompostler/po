using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using po.Extensions;
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
            this.discordClient.Log += this.DiscordClient_Log;

            await this.discordClient.LoginAsync(Discord.TokenType.Bot, this.options.Value.BotToken);
            await this.discordClient.StartAsync();
        }

        private Task DiscordClient_Log(Discord.LogMessage arg)
        {
            switch (arg.Severity)
            {
                case Discord.LogSeverity.Critical:
                    this.logger.LogCritical(arg.Exception, arg.Message);
                    break;

                case Discord.LogSeverity.Error:
                    this.logger.LogError(arg.Exception, arg.Message);
                    break;

                case Discord.LogSeverity.Warning:
                    this.logger.LogWarning(arg.Exception, arg.Message);
                    break;

                case Discord.LogSeverity.Info:
                    this.logger.LogInformation(arg.Exception, arg.Message);
                    break;

                case Discord.LogSeverity.Verbose:
                    this.logger.LogTrace(arg.Exception, arg.Message);
                    break;

                case Discord.LogSeverity.Debug:
                    this.logger.LogDebug(arg.Exception, arg.Message);
                    break;

                default:
                    this.logger.LogInformation($"Uknown log severity: {arg.ToJsonString()}");
                    break;
            }

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await this.discordClient.StopAsync();
            await this.discordClient.LogoutAsync();
        }
    }
}
