using Microsoft.ApplicationInsights;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using po.DataAccess;
using po.Extensions;
using po.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace po.Services.Background
{
    public sealed class SyncBlobMetadataBackgroundService : SqlSynchronizedBackgroundService
    {
        private const int BatchSize = 100;

        private readonly PoStorage storage;
        private readonly Options.Discord discordOptions;

        public SyncBlobMetadataBackgroundService(
            IServiceProvider serviceProvider,
            Sentinals sentinals,
            ILogger<SyncBlobMetadataBackgroundService> logger,
            TelemetryClient telemetryClient,
            PoStorage storage,
            IOptions<Options.Discord> options)
            : base(serviceProvider, sentinals, logger, telemetryClient)
        {
            this.storage = storage;
            this.discordOptions = options.Value;
        }

        protected override TimeSpan Interval => TimeSpan.FromDays(1);

        public sealed class CountValue
        {
            public uint Added;
            public uint Removed;
            public uint Total;
        }

        protected override async Task ExecuteOnceAsync(CancellationToken cancellationToken)
        {
            Dictionary<string, CountValue> counts = new();

            // Add new blobs
            await FindAndAddNewBlobsAsync(this.serviceProvider, this.storage, this.logger, counts, cancellationToken);

            // Remove any that we haven't seen for more than 3 scrape intervals
            DateTimeOffset tooOld = DateTimeOffset.UtcNow.AddDays(this.Interval.TotalDays * -3);
            using (IServiceScope scope = this.serviceProvider.CreateScope())
            using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
            {
                List<Models.PoBlob> toDeletes = await poContext.Blobs.Where(x => x.LastSeen < tooOld).ToListAsync(cancellationToken);

                if (toDeletes.Count > BatchSize)
                {
                    this.logger.LogWarning($"Found {toDeletes.Count} old blobs to remove. Only removing {BatchSize} to avoid SQL timeout.");
                    toDeletes = toDeletes.Take(BatchSize).ToList();
                }
                if (toDeletes.Count > 0)
                {
                    this.logger.LogWarning($"Found {toDeletes.Count} old blobs to remove: {string.Join(", ", toDeletes.Select(x => x.ToString()))}");
                }
                else
                {
                    this.logger.LogInformation("Found no old blobs to remove.");
                }

                foreach (Models.PoBlob toDelete in toDeletes)
                {
                    if (!counts.ContainsKey(toDelete.Category))
                    {
                        counts.Add(toDelete.Category, new CountValue());
                    }
                    counts[toDelete.Category].Removed++;
                    _ = poContext.Blobs.Remove(toDelete);
                }

                _ = await poContext.SaveChangesAsync(cancellationToken);
            }

            // Report on what we did
            string report = BuildReportFromCounts(counts, this.logger);
            if (report != default)
            {
                Discord.WebSocket.DiscordSocketClient discordClient = await this.sentinals.DiscordClient.WaitForCompletionAsync(cancellationToken);
                await discordClient.TrySendNotificationTextMessageOrFileAsync(this.discordOptions, report, this.logger, cancellationToken);
            }
        }

        public static string BuildReportFromCounts(Dictionary<string, CountValue> counts, ILogger logger)
        {
            if (counts.Values.Any(x => x.Added > 0 || x.Removed > 0))
            {
                int catLen = Math.Max("category".Length, counts.Keys.Max(x => x.Length));
                int numLen = "removed".Length;
                StringBuilder sb = new();
                _ = sb.AppendLine("```");
                _ = sb.AppendLine($"{"CATEGORY".PadRight(catLen)}  {"ADDED".PadLeft(numLen)}  {"REMOVED".PadLeft(numLen)}  {"TOTAL".PadLeft(numLen)}");
                _ = sb.AppendLine($"{new string('=', catLen)}  {new string('=', numLen)}  {new string('=', numLen)}  {new string('=', numLen)}");
                foreach (KeyValuePair<string, CountValue> count in counts.Where(x => x.Value.Added > 0 || x.Value.Removed > 0))
                {
                    _ = sb.AppendLine($"{count.Key.PadRight(catLen)}  {count.Value.Added.ToString().PadLeft(numLen)}  {count.Value.Removed.ToString().PadLeft(numLen)}  {count.Value.Total.ToString().PadLeft(numLen)}");
                }
                _ = sb.AppendLine($"{"TOTAL".PadRight(catLen)}  {counts.Values.Sum(x => x.Added).ToString().PadLeft(numLen)}  {counts.Values.Sum(x => x.Removed).ToString().PadLeft(numLen)}  {counts.Values.Sum(x => x.Total).ToString().PadLeft(numLen)}");
                _ = sb.AppendLine("```");
                return sb.ToString();
            }
            else
            {
                logger.LogInformation("Nothing new to report.");
                return default;
            }
        }

        public static async Task FindAndAddNewBlobsAsync(IServiceProvider serviceProvider, PoStorage storage, ILogger logger, Dictionary<string, CountValue> counts, CancellationToken cancellationToken, string containerName = default)
        {
            List<Models.PoBlob> foundBlobs = new(BatchSize);

            // Add new ones
            IAsyncEnumerable<Models.PoBlob> enumerable = containerName == default
                                                        ? storage.EnumerateAllBlobsAsync(cancellationToken, goSlow: true)
                                                        : storage.EnumerateAllBlobsAsync(containerName, cancellationToken);
            await foreach (Models.PoBlob foundBlob in enumerable)
            {
                foundBlobs.Add(foundBlob);

                if (foundBlobs.Count >= BatchSize)
                {
                    await ReconcileFoundBlobsInBatchAsync(serviceProvider, foundBlobs, counts, cancellationToken);
                    foundBlobs.Clear();
                }
            }
            if (foundBlobs.Count > 0)
            {
                await ReconcileFoundBlobsInBatchAsync(serviceProvider, foundBlobs, counts, cancellationToken);
            }
        }

        private static async Task ReconcileFoundBlobsInBatchAsync(IServiceProvider serviceProvider, List<Models.PoBlob> foundBlobs, Dictionary<string, CountValue> counts, CancellationToken cancellationToken)
        {
            using (IServiceScope scope = serviceProvider.CreateScope())
            using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
            {
                foreach (Models.PoBlob foundBlob in foundBlobs)
                {
                    Models.PoBlob existingBlob = await poContext.Blobs.SingleOrDefaultAsync(x => x.AccountName == foundBlob.AccountName && x.ContainerName == foundBlob.ContainerName && x.Name == foundBlob.Name, cancellationToken);
                    if (!counts.ContainsKey(foundBlob.Category))
                    {
                        counts.Add(foundBlob.Category, new CountValue());
                    }
                    if (existingBlob == default)
                    {
                        counts[foundBlob.Category].Added++;
                        _ = poContext.Blobs.Add(foundBlob);
                    }
                    else
                    {
                        existingBlob.CopyFrom(foundBlob);
                    }
                    counts[foundBlob.Category].Total++;
                }

                _ = await poContext.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
