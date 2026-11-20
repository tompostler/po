using Microsoft.ApplicationInsights;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using po.DataAccess;
using po.Extensions;
using po.Services.Po;
using po.Utilities;

namespace po.Services.Background
{
    public sealed class SyncBlobMetadataBackgroundService : SqlSynchronizedBackgroundService
    {
        private const int BatchSize = 100;
        private static readonly TimeSpan MinInterval = TimeSpan.FromHours(2);

        private readonly PoBlobSync poBlobSync;
        private readonly Options.Discord discordOptions;

        public SyncBlobMetadataBackgroundService(
            IServiceProvider serviceProvider,
            Sentinels sentinels,
            ILogger<SyncBlobMetadataBackgroundService> logger,
            TelemetryClient telemetryClient,
            PoBlobSync poBlobSync,
            IOptions<Options.Discord> options)
            : base(serviceProvider, sentinels, logger, telemetryClient)
        {
            this.poBlobSync = poBlobSync;
            this.discordOptions = options.Value;
        }

        private TimeSpan interval = TimeSpan.FromDays(1);
        protected override TimeSpan Interval => this.interval;

        protected override async Task ExecuteOnceAsync(CancellationToken cancellationToken)
        {
            Dictionary<string, PoBlobSync.CountValue> counts = new();

            // Add new blobs
            await this.poBlobSync.FindAndAddNewBlobsAsync(counts, cancellationToken);

            // Remove any that we haven't seen for more than 3 scrape intervals
            DateTimeOffset tooOld = DateTimeOffset.UtcNow.AddDays(this.interval.TotalDays * -3);
            using (IServiceScope scope = this.serviceProvider.CreateScope())
            using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
            {
                List<Models.PoBlob> toDeletes = await poContext.Blobs.Where(x => x.LastSeen < tooOld).ToListAsync(cancellationToken);

                if (toDeletes.Count > BatchSize)
                {
                    this.interval = TimeSpan.FromHours(Math.Max(MinInterval.TotalHours, this.interval.TotalHours / 2));
                    this.logger.LogWarning($"Found {toDeletes.Count} old blobs to remove. Only removing {BatchSize} to avoid SQL timeout and reducing interval to {this.interval}.");
                    toDeletes = toDeletes.Take(BatchSize).ToList();
                }
                if (toDeletes.Count > 0)
                {
                    this.logger.LogWarning($"Found {toDeletes.Count} old blobs to remove: {string.Join(", ", toDeletes.Select(x => x.ToString()))}");
                }
                else
                {
                    this.logger.LogInformation("Found no old blobs to remove.");
                    this.interval = TimeSpan.FromDays(1);
                }

                foreach (Models.PoBlob toDelete in toDeletes)
                {
                    if (!counts.TryGetValue(toDelete.Category, out PoBlobSync.CountValue value))
                    {
                        value = new PoBlobSync.CountValue();
                        counts.Add(toDelete.Category, value);
                    }

                    value.Removed++;
                    _ = poContext.Blobs.Remove(toDelete);
                }

                _ = await poContext.SaveChangesAsync(cancellationToken);
            }

            // Report on what we did
            string report = this.poBlobSync.BuildReportFromCounts(counts, $"Running again in {this.interval.TotalHours:0.0}h.");
            if (report != default)
            {
                Discord.WebSocket.DiscordSocketClient discordClient = await this.sentinels.DiscordClient.WaitForCompletionAsync(cancellationToken);
                await discordClient.TrySendNotificationTextMessageOrFileAsync(this.discordOptions, report, this.logger, cancellationToken);
            }
        }
    }
}
