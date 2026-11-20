using Microsoft.EntityFrameworkCore;
using po.DataAccess;

namespace po.Services.Po
{
    public sealed class PoImageService
    {
        /// <summary>
        /// How long a posted image link stays valid.
        /// Chat clients load the image from the link whenever the message is viewed, so a short lifetime breaks images in channel history.
        /// </summary>
        private static readonly TimeSpan ImageLinkLifetime = TimeSpan.FromDays(7);

        private readonly IServiceProvider serviceProvider;
        private readonly IPoStorage poStorage;

        public PoImageService(
            IServiceProvider serviceProvider,
            IPoStorage poStorage)
        {
            this.serviceProvider = serviceProvider;
            this.poStorage = poStorage;
        }

        public static string NoneRemainingMessage(string category) => $"Category prefix `{category ?? "(any)"}` has no images remaining. Try `/po reset category`";

        /// <summary>
        /// Picks a random unseen image whose category starts with <paramref name="category"/>.
        /// Does not mark it seen; call <see cref="MarkSeenAsync"/> once it has been shown.
        /// </summary>
        /// <returns>The picked image, or null if none are remaining.</returns>
        public async Task<PoImage> PickUnseenAsync(string containerName, string category, string username, TimeSpan? nextImageIn = default)
        {
            using IServiceScope scope = this.serviceProvider.CreateScope();
            using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();

            Models.PoBlob blob = await poContext.Blobs
                            .Where(x => x.ContainerName == containerName && x.Category.StartsWith(category ?? string.Empty) && !x.Seen)
                            .OrderBy(x => Guid.NewGuid())
                            .FirstOrDefaultAsync();

            if (blob == default)
            {
                return null;
            }

            var counts = await poContext.Blobs
                        .Where(x => x.ContainerName == containerName && x.Category.StartsWith(category ?? string.Empty) && !x.Seen)
                        .GroupBy(x => x.Category)
                        .Select(g => new
                        {
                            g.Key,
                            CountUnseen = g.Count()
                        })
                        .ToListAsync();
            double chance = 1.0 * counts.Single(c => c.Key == blob.Category).CountUnseen / counts.Sum(c => c.CountUnseen);

            string nextImageInText = string.Empty;
            if (nextImageIn != default)
            {
                nextImageInText = "\nNext image in ";
                if (nextImageIn.Value.TotalHours > 1)
                {
                    nextImageInText += nextImageIn.Value.TotalHours.ToString("0.0");
                    nextImageInText += 'h';
                }
                else
                {
                    nextImageInText += nextImageIn.Value.TotalMinutes.ToString("0.0");
                    nextImageInText += 'm';
                }
            }

            return new PoImage(
                blob,
                blob.Name,
                $"Request: `{category ?? "(any)"}` ({username})\nResponse category chance: {chance:P2}{nextImageInText}",
                this.poStorage.GetReadUri(blob, ImageLinkLifetime));
        }

        /// <summary>
        /// Marks every seen image whose category starts with <paramref name="category"/> as unseen again.
        /// Calls <paramref name="onCategoryReset"/> with a message after each category.
        /// </summary>
        /// <returns>A summary of the total reset.</returns>
        public async Task<string> ResetSeenAsync(string containerName, string category, Func<string, Task> onCategoryReset)
        {
            using IServiceScope scope = this.serviceProvider.CreateScope();
            using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();

            List<string> categoriesToReset = await poContext.Blobs
                        .Where(x => x.ContainerName == containerName && x.Category.StartsWith(category ?? string.Empty) && x.Seen)
                        .GroupBy(x => x.Category)
                        .Select(g => g.Key)
                        .ToListAsync();

            int totalCountReset = 0;
            foreach (string categoryToReset in categoriesToReset)
            {
                int countReset = await poContext.Blobs
                    .Where(x => x.ContainerName == containerName && x.Category == categoryToReset && x.Seen)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Seen, false));
                totalCountReset += countReset;
                await onCategoryReset($"Reset view status for {countReset} images in the `{categoryToReset}` category.");
            }

            return $"Reset view status for {totalCountReset} total images across {categoriesToReset.Count} categories.";
        }

        public async Task MarkSeenAsync(PoImage image)
        {
            using IServiceScope scope = this.serviceProvider.CreateScope();
            using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();

            _ = await poContext.Blobs
                .Where(x => x.AccountName == image.Blob.AccountName && x.ContainerName == image.Blob.ContainerName && x.Name == image.Blob.Name)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Seen, true));
        }
    }
}
