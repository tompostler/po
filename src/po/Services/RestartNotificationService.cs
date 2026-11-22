using System.Collections;
using System.Security.Cryptography;
using System.Text;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using po.Chat;

namespace po.Services
{
    public class RestartNotificationService : BackgroundService
    {
        private readonly ChatRouter chatRouter;
        private readonly TelemetryClient telemetryClient;

        public RestartNotificationService(
            ChatRouter chatRouter,
            TelemetryClient telemetryClient)
        {
            this.chatRouter = chatRouter;
            this.telemetryClient = telemetryClient;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using IOperationHolder<RequestTelemetry> op = this.telemetryClient.StartOperation<RequestTelemetry>(this.GetType().FullName);

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

            await this.chatRouter.SendNotificationTextAsync(
                $"I have been restarted on {Environment.MachineName}. v{ThisAssembly.AssemblyInformationalVersion}. env({envVars.Count}):{envHashSubstring}",
                stoppingToken);
        }
    }
}
