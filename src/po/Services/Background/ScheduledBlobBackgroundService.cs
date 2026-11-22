using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.EntityFrameworkCore;
using po.Chat;
using po.DataAccess;
using po.Services.Po;
using po.Utilities;

namespace po.Services.Background
{
    public sealed class ScheduledBlobBackgroundService : BackgroundService
    {
        private readonly IServiceProvider serviceProvider;
        private readonly PoImageService poImageService;
        private readonly ChatRouter chatRouter;
        private readonly Delays delays;
        private readonly Sentinels sentinels;
        private readonly ILogger<ScheduledBlobBackgroundService> logger;
        private readonly TelemetryClient telemetryClient;

        public ScheduledBlobBackgroundService(
            IServiceProvider serviceProvider,
            PoImageService poImageService,
            ChatRouter chatRouter,
            Delays delays,
            Sentinels sentinels,
            ILogger<ScheduledBlobBackgroundService> logger,
            TelemetryClient telemetryClient)
        {
            this.serviceProvider = serviceProvider;
            this.poImageService = poImageService;
            this.chatRouter = chatRouter;
            this.delays = delays;
            this.sentinels = sentinels;
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
                        List<Models.ScheduledBlob> nextScheduledBlobs = await poContext.ScheduledBlobs
                                                                    .OrderBy(x => x.ScheduledDate)
                                                                    .Take(2)
                                                                    .ToListAsync(stoppingToken);
                        Models.ScheduledBlob nextScheduledBlob = nextScheduledBlobs.FirstOrDefault();

                        if (nextScheduledBlob?.ScheduledDate < DateTimeOffset.UtcNow)
                        {
                            ChannelRef channel = nextScheduledBlob.Channel;

                            TimeSpan? nextBlobIn = default;
                            if (nextScheduledBlobs.Count == 2)
                            {
                                nextBlobIn = nextScheduledBlobs.Last().ScheduledDate - nextScheduledBlob.ScheduledDate;
                            }

                            PoImage image = await this.poImageService.PickUnseenAsync(
                                nextScheduledBlob.ContainerName,
                                nextScheduledBlob.Category,
                                nextScheduledBlob.Username,
                                nextBlobIn);
                            if (image == default)
                            {
                                await this.chatRouter.SendTextAsync(channel, PoImageService.NoneRemainingMessage(nextScheduledBlob.Category), stoppingToken);
                            }
                            else
                            {
                                await this.chatRouter.SendMessageAsync(channel, image.ToChatMessage(), stoppingToken);
                                await this.poImageService.MarkSeenAsync(image);
                            }

                            _ = poContext.ScheduledBlobs.Remove(nextScheduledBlob);
                            _ = await poContext.SaveChangesAsync(stoppingToken);
                            delay = TimeSpan.FromMinutes(1);
                        }
                        else if (nextScheduledBlob != default)
                        {
                            // If there's no current blob to show, let the delay be until the next blob
                            delay = nextScheduledBlob.ScheduledDate.Subtract(DateTimeOffset.UtcNow);
                        }
                    }
                    // The delay should be at least 1 minute
                    delay = TimeSpan.FromMinutes(Math.Max(1, delay.TotalMinutes));
                    // But less than a day
                    delay = TimeSpan.FromDays(Math.Min(1, delay.TotalDays));
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, "Could not check/handle scheduled image.");
                }

                await this.delays.ScheduledBlob.Delay(delay, this.logger, stoppingToken);
            }
        }
    }
}
