using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.EntityFrameworkCore;
using po.Chat;
using po.DataAccess;
using po.Utilities;

namespace po.Services.Background
{
    public sealed class RandomMessageBackgroundService : BackgroundService
    {
        private readonly IServiceProvider serviceProvider;
        private readonly ChatRouter chatRouter;
        private readonly Delays delays;
        private readonly Sentinels sentinels;
        private readonly ILogger<RandomMessageBackgroundService> logger;
        private readonly TelemetryClient telemetryClient;

        public RandomMessageBackgroundService(
            IServiceProvider serviceProvider,
            ChatRouter chatRouter,
            Delays delays,
            Sentinels sentinels,
            ILogger<RandomMessageBackgroundService> logger,
            TelemetryClient telemetryClient)
        {
            this.serviceProvider = serviceProvider;
            this.chatRouter = chatRouter;
            this.delays = delays;
            this.sentinels = sentinels;
            this.logger = logger;
            this.telemetryClient = telemetryClient;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await this.sentinels.DBMigration.WaitForCompletionAsync(stoppingToken);

            var delay = TimeSpan.FromMinutes(1);
            while (!stoppingToken.IsCancellationRequested)
            {
                using IOperationHolder<RequestTelemetry> op = this.telemetryClient.StartOperation<RequestTelemetry>(this.GetType().FullName);
                try
                {
                    using (IServiceScope scope = this.serviceProvider.CreateScope())
                    using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
                    {
                        Models.RandomMessage randomMessage = await poContext.RandomMessages.OrderBy(x => x.Id).FirstOrDefaultAsync(stoppingToken);

                        if (randomMessage != default)
                        {
                            var message = new ChatMessage
                            {
                                Title = randomMessage.Title.Trim(),
                                Text = randomMessage.Description?.Trim(),
                                ColorHex = ChooseColorHex(randomMessage),
                                Footer = $"random message {randomMessage.Id}",
                            };

                            if (!await this.chatRouter.SendNotificationMessageAsync(message, stoppingToken))
                            {
                                throw new InvalidOperationException($"Random message {randomMessage.Id} was not sent to any notification channel, so it stays queued.");
                            }

                            _ = poContext.RandomMessages.Remove(randomMessage);
                            _ = await poContext.SaveChangesAsync(stoppingToken);

                            // Reset the delay
                            delay = TimeSpan.Zero;
                        }
                        else
                        {
                            // Exponential backoff sleep
                            delay *= 2;
                        }
                    }
                    // The delay should be at least 1 minute
                    delay = TimeSpan.FromMinutes(Math.Max(1, delay.TotalMinutes));
                    // but less than 1 hour
                    delay = TimeSpan.FromHours(Math.Min(1, delay.TotalHours));
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, "Could not send message.");
                }

                await this.delays.RandomMessage.Delay(delay, this.logger, stoppingToken);
            }
        }

        /// <summary>
        /// Uses the sender's color if given, otherwise red or green when the title reads like a failure or success report.
        /// </summary>
        private static string ChooseColorHex(Models.RandomMessage randomMessage)
        {
            if (!string.IsNullOrWhiteSpace(randomMessage.Color))
            {
                return randomMessage.Color.Trim();
            }
            else if (randomMessage.Title.Contains("not successful") || randomMessage.Title.Contains("unsuccessful") || randomMessage.Title.Contains("failed"))
            {
                // A soft red
                return "#E74C3C";
            }
            else if (randomMessage.Title.Contains("successful"))
            {
                // A soft green
                return "#2ECC71";
            }

            return default;
        }
    }
}
