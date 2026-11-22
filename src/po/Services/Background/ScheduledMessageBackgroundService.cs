using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.EntityFrameworkCore;
using po.Chat;
using po.DataAccess;
using po.Utilities;

namespace po.Services.Background
{
    public sealed class ScheduledMessageBackgroundService : BackgroundService
    {
        private readonly IServiceProvider serviceProvider;
        private readonly Delays delays;
        private readonly Sentinels sentinels;
        private readonly ChatRouter chatRouter;
        private readonly ILogger<ScheduledMessageBackgroundService> logger;
        private readonly TelemetryClient telemetryClient;

        public ScheduledMessageBackgroundService(
            IServiceProvider serviceProvider,
            Delays delays,
            Sentinels sentinels,
            ChatRouter chatRouter,
            ILogger<ScheduledMessageBackgroundService> logger,
            TelemetryClient telemetryClient)
        {
            this.serviceProvider = serviceProvider;
            this.delays = delays;
            this.sentinels = sentinels;
            this.chatRouter = chatRouter;
            this.logger = logger;
            this.telemetryClient = telemetryClient;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await this.sentinels.DBMigration.WaitForCompletionAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromDays(1);
                using IOperationHolder<RequestTelemetry> op = this.telemetryClient.StartOperation<RequestTelemetry>(this.GetType().FullName);
                try
                {
                    using (IServiceScope scope = this.serviceProvider.CreateScope())
                    using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
                    {
                        Models.ScheduledMessage nextScheduledMessage = await poContext.ScheduledMessages
                                                                                .OrderBy(x => x.ScheduledDate)
                                                                                .FirstOrDefaultAsync(stoppingToken);

                        if (nextScheduledMessage?.ScheduledDate < DateTimeOffset.UtcNow)
                        {
                            // Plain text reads like the reminder it usually is, rather than a card.
                            // The name has no @, so posting doesn't ping the person who scheduled it, and asterisks keep an underscore in a username from ending the italics.
                            await this.chatRouter.SendTextAsync(
                                nextScheduledMessage.Channel,
                                $"{nextScheduledMessage.Message}\n*scheduled by {nextScheduledMessage.Author}*",
                                stoppingToken);

                            _ = poContext.ScheduledMessages.Remove(nextScheduledMessage);
                            _ = await poContext.SaveChangesAsync(stoppingToken);
                            delay = TimeSpan.FromMinutes(1);
                        }
                        else if (nextScheduledMessage != default)
                        {
                            // If there's no current message to send, let the delay be until the next message
                            delay = nextScheduledMessage.ScheduledDate.Subtract(DateTimeOffset.UtcNow);
                        }
                    }
                    // The delay should be at least 1 minute
                    delay = TimeSpan.FromMinutes(Math.Max(1, delay.TotalMinutes));
                    // But less than a day
                    delay = TimeSpan.FromDays(Math.Min(1, delay.TotalDays));
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, "Could not check/handle scheduled message.");
                }

                await this.delays.ScheduledMessage.Delay(delay, this.logger, stoppingToken);
            }
        }
    }
}
