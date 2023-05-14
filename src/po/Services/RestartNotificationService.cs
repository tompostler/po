using Discord.WebSocket;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Options;
using po.Extensions;
using po.Utilities;

namespace po.Services
{
    public class RestartNotificationService : BackgroundService
    {
        private readonly Sentinals sentinals;
        private readonly Options.Discord discordOptions;
        private readonly ILogger<RestartNotificationService> logger;
        private readonly TelemetryClient telemetryClient;

        public RestartNotificationService(
            Sentinals sentinals,
            IOptions<Options.Discord> discordOptions,
            ILogger<RestartNotificationService> logger,
            TelemetryClient telemetryClient)
        {
            this.sentinals = sentinals;
            this.discordOptions = discordOptions.Value;
            this.logger = logger;
            this.telemetryClient = telemetryClient;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using IOperationHolder<RequestTelemetry> op = this.telemetryClient.StartOperation<RequestTelemetry>(this.GetType().FullName);

            DiscordSocketClient discordClient = await this.sentinals.DiscordClient.WaitForCompletionAsync(stoppingToken);

            await discordClient.TrySendNotificationTextMessageOrFileAsync(
                this.discordOptions,
                $"I have been restarted on {Environment.MachineName}. v{typeof(BotService).Assembly.GetName().Version.ToString(3)}",
                this.logger,
                stoppingToken);
        }
    }
}
