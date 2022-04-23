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
        private readonly PoBlobStorage storage;
        private readonly Options.Discord discordOptions;

        public SyncBlobMetadataBackgroundService(
            IServiceProvider serviceProvider,
            Sentinals sentinals,
            ILogger<SyncBlobMetadataBackgroundService> logger,
            PoBlobStorage storage,
            IOptions<Options.Discord> options)
            : base(serviceProvider, sentinals, logger)
        {
            this.storage = storage;
            this.discordOptions = options.Value;
        }

        protected override TimeSpan Interval => TimeSpan.FromDays(1);

        private readonly Dictionary<string, CountValue> Counts = new();
        private sealed class CountValue
        {
            public uint Added;
            public uint Removed;
            public uint Total;
        }

        protected override async Task ExecuteOnceAsync(CancellationToken cancellationToken)
        {
            const int batchSize = 100;

            List<Models.PoBlob> foundBlobs = new(batchSize);
            this.Counts.Clear();

            // Add new ones
            await foreach (Models.PoBlob foundBlob in this.storage.EnumerateAllBlobsAsync(cancellationToken))
            {
                foundBlobs.Add(foundBlob);

                if (foundBlobs.Count >= batchSize)
                {
                    await this.ReconcileFoundBlobsInBatchAsync(foundBlobs, cancellationToken);
                    foundBlobs.Clear();
                }
            }
            if (foundBlobs.Count > 0)
            {
                await this.ReconcileFoundBlobsInBatchAsync(foundBlobs, cancellationToken);
            }

            // Remove any that we haven't seen for more than 3 scrape intervals
            DateTimeOffset tooOld = DateTimeOffset.UtcNow.AddDays(this.Interval.TotalDays * -3);
            using (IServiceScope scope = this.serviceProvider.CreateScope())
            using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
            {
                List<Models.PoBlob> toDeletes = await poContext.Blobs.Where(x => x.LastSeen < tooOld).ToListAsync(cancellationToken);

                if (toDeletes.Count > batchSize)
                {
                    this.logger.LogWarning($"Found {toDeletes.Count} old blobs to remove. Only removing {batchSize} to avoid SQL timeout.");
                    toDeletes = toDeletes.Take(batchSize).ToList();
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
                    if (!this.Counts.ContainsKey(toDelete.Category))
                    {
                        this.Counts.Add(toDelete.Category, new CountValue());
                    }
                    this.Counts[toDelete.Category].Removed++;
                    _ = poContext.Blobs.Remove(toDelete);
                }

                _ = await poContext.SaveChangesAsync(cancellationToken);
            }

            // Report on what we did
            if (this.Counts.Values.Any(x => x.Added > 0 || x.Removed > 0))
            {
                int catLen = Math.Max("category".Length, this.Counts.Keys.Max(x => x.Length));
                int numLen = "removed".Length;
                StringBuilder sb = new();
                _ = sb.AppendLine("```");
                _ = sb.AppendLine($"{"CATEGORY".PadRight(catLen)}  {"ADDED".PadLeft(numLen)}  {"REMOVED".PadLeft(numLen)}  {"TOTAL".PadLeft(numLen)}");
                _ = sb.AppendLine($"{new string('=', catLen)}  {new string('=', numLen)}  {new string('=', numLen)}  {new string('=', numLen)}");
                foreach (KeyValuePair<string, CountValue> count in this.Counts.Where(x => x.Value.Added > 0 || x.Value.Removed > 0))
                {
                    _ = sb.AppendLine($"{count.Key.PadRight(catLen)}  {count.Value.Added.ToString().PadLeft(numLen)}  {count.Value.Removed.ToString().PadLeft(numLen)}  {count.Value.Total.ToString().PadLeft(numLen)}");
                }
                _ = sb.AppendLine($"{"TOTAL".PadRight(catLen)}  {this.Counts.Values.Sum(x => x.Added).ToString().PadLeft(numLen)}  {this.Counts.Values.Sum(x => x.Removed).ToString().PadLeft(numLen)}  {this.Counts.Values.Sum(x => x.Total).ToString().PadLeft(numLen)}");
                _ = sb.AppendLine("```");

                Discord.WebSocket.DiscordSocketClient discordClient = await this.sentinals.DiscordClient.WaitForCompletionAsync(cancellationToken);
                await discordClient.TrySendNotificationTextMessageAsync(this.discordOptions, sb.ToString(), this.logger, cancellationToken);
            }
            else
            {
                this.logger.LogInformation("Nothing new to report.");
            }
        }

        private async Task ReconcileFoundBlobsInBatchAsync(List<Models.PoBlob> foundBlobs, CancellationToken cancellationToken)
        {
            using (IServiceScope scope = this.serviceProvider.CreateScope())
            using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
            {
                foreach (Models.PoBlob foundBlob in foundBlobs)
                {
                    Models.PoBlob existingBlob = await poContext.Blobs.SingleOrDefaultAsync(x => x.AccountName == foundBlob.AccountName && x.ContainerName == foundBlob.ContainerName && x.Name == foundBlob.Name, cancellationToken);
                    if (!this.Counts.ContainsKey(foundBlob.Category))
                    {
                        this.Counts.Add(foundBlob.Category, new CountValue());
                    }
                    if (existingBlob == default)
                    {
                        this.Counts[foundBlob.Category].Added++;
                        _ = poContext.Blobs.Add(foundBlob);
                    }
                    else
                    {
                        existingBlob.CopyFrom(foundBlob);
                    }
                    this.Counts[foundBlob.Category].Total++;
                }

                _ = await poContext.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
