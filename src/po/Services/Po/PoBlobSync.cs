using Microsoft.EntityFrameworkCore;
using po.DataAccess;
using System.Text;

namespace po.Services.Po
{
    /// <summary>
    /// Reconciles the blobs recorded in the database with the blobs in storage.
    /// </summary>
    public sealed class PoBlobSync
    {
        private const int BatchSize = 100;

        private readonly IServiceProvider serviceProvider;
        private readonly IPoStorage storage;
        private readonly ILogger<PoBlobSync> logger;

        public PoBlobSync(
            IServiceProvider serviceProvider,
            IPoStorage storage,
            ILogger<PoBlobSync> logger)
        {
            this.serviceProvider = serviceProvider;
            this.storage = storage;
            this.logger = logger;
        }

        public sealed class CountValue
        {
            public uint Added;
            public uint Removed;
            public uint Total;
        }

        /// <summary>
        /// Builds a table of added, removed and total blobs per category, as a markdown code block.
        /// </summary>
        /// <returns>The report, or null if nothing was added or removed.</returns>
        public string BuildReportFromCounts(Dictionary<string, CountValue> counts, string suffix = null)
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
                if (suffix != null)
                {
                    _ = sb.AppendLine();
                    _ = sb.AppendLine(suffix);
                }
                return sb.ToString();
            }
            else
            {
                this.logger.LogInformation("Nothing new to report.");
                return default;
            }
        }

        /// <summary>
        /// Adds blobs found in storage that aren't in the database yet, and refreshes the metadata of ones that are.
        /// Scans every container, slowly, unless <paramref name="containerName"/> is given.
        /// </summary>
        public async Task FindAndAddNewBlobsAsync(Dictionary<string, CountValue> counts, CancellationToken cancellationToken, string containerName = default)
        {
            List<Models.PoBlob> foundBlobs = new(BatchSize);

            // Add new ones
            IAsyncEnumerable<Models.PoBlob> enumerable = containerName == default
                                                        ? this.storage.EnumerateAllBlobsAsync(cancellationToken, goSlow: true)
                                                        : this.storage.EnumerateAllBlobsAsync(containerName, cancellationToken);
            await foreach (Models.PoBlob foundBlob in enumerable)
            {
                foundBlobs.Add(foundBlob);

                if (foundBlobs.Count >= BatchSize)
                {
                    await this.ReconcileFoundBlobsInBatchAsync(foundBlobs, counts, cancellationToken);
                    foundBlobs.Clear();
                }
            }
            if (foundBlobs.Count > 0)
            {
                await this.ReconcileFoundBlobsInBatchAsync(foundBlobs, counts, cancellationToken);
            }
        }

        private async Task ReconcileFoundBlobsInBatchAsync(List<Models.PoBlob> foundBlobs, Dictionary<string, CountValue> counts, CancellationToken cancellationToken)
        {
            using (IServiceScope scope = this.serviceProvider.CreateScope())
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
