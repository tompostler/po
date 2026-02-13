using System.Collections;
using System.Security.Cryptography;
using System.Text;
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

            IDictionary envVars = Environment.GetEnvironmentVariables();
            var envContent = new StringBuilder();
            foreach (string key in envVars.Keys.Cast<string>().OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                _ = envContent
                    .Append(key)
                    .Append('=')
                    .Append(envVars[key])
                    .Append('\n');
            }
            string envHashSubstring = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(envContent.ToString())))[..12];

            await discordClient.TrySendNotificationTextMessageOrFileAsync(
                this.discordOptions,
                $"I have been restarted on {Environment.MachineName}. v{ThisAssembly.AssemblyInformationalVersion}. env({envVars.Count}):{envHashSubstring}",
                this.logger,
                stoppingToken);
        }
    }
}
