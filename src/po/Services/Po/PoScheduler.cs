using Microsoft.EntityFrameworkCore;
using po.DataAccess;
using po.Models;
using po.Utilities;

namespace po.Services.Po
{
    /// <summary>
    /// Schedules images for later display by <see cref="Background.ScheduledBlobBackgroundService"/>.
    /// Each method returns the user-facing response, whether it's a validation error or a confirmation.
    /// </summary>
    public sealed class PoScheduler
    {
        private readonly IServiceProvider serviceProvider;
        private readonly Delays delays;

        public PoScheduler(
            IServiceProvider serviceProvider,
            Delays delays)
        {
            this.serviceProvider = serviceProvider;
            this.delays = delays;
        }

        public async Task<string> ClearAsync(string containerName, ulong channelId)
        {
            using IServiceScope scope = this.serviceProvider.CreateScope();
            using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();

            int scheduledBlobsDeleted = await poContext.ScheduledBlobs.Where(x => x.ContainerName == containerName && x.ChannelId == channelId).ExecuteDeleteAsync();

            return $"Canceled {scheduledBlobsDeleted} scheduled image(s).";
        }

        /// <summary>
        /// Schedules <paramref name="countRequested"/> images at random times over <paramref name="durationInput"/>, plus one immediately.
        /// </summary>
        public async Task<string> ScheduleRandomAsync(string containerName, ulong channelId, string category, string username, long? countRequested, string durationInput)
        {
            using IServiceScope scope = this.serviceProvider.CreateScope();
            using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();

            string errorMessage = default;

            if (!countRequested.HasValue)
            {
                errorMessage = "`count` must be supplied.";
            }
            else if (countRequested <= 0 || countRequested > 200)
            {
                errorMessage = $"Count must be between 0 and 200. (Is currently `{countRequested}`)";
            }

            if (string.IsNullOrEmpty(durationInput))
            {
                errorMessage = "`duration` must be supplied.";
            }
            (string msg, TimeSpan parsed) duration = DurationParser.Parse(durationInput);
            if (duration.msg != default)
            {
                errorMessage = duration.msg;
            }
            else if (duration.parsed > TimeSpan.FromDays(30))
            {
                errorMessage = $"Duration cannot last more than 30 days. (Is currently `{duration.parsed}`)";
            }
            else if (duration.parsed.TotalMinutes / countRequested < 5)
            {
                errorMessage = "Cannot randomize more than 1 image per 5 minutes.";
            }

            int countAvailable = await poContext.Blobs.CountAsync(x => x.ContainerName == containerName && x.Category.StartsWith(category ?? string.Empty) && !x.Seen);
            if (countRequested > countAvailable * 1.1)
            {
                errorMessage = $"Requested to schedule {countRequested}, but only {countAvailable} are available (including a 10% buffer). Request fewer scheduled images.";
            }

            if (errorMessage != default)
            {
                return errorMessage;
            }

            // Create the random intervals (and sort them for sequential counting)
            var delays = new TimeSpan[countRequested.Value + 1];
            delays[0] = TimeSpan.Zero;
            for (int i = 1; i < delays.Length; i++)
            {
                delays[i] = TimeSpan.FromMinutes(duration.parsed.TotalMinutes * Random.Shared.NextDouble());
            }
            Array.Sort(delays);

            // Actually schedule the delays
            for (int i = 0; i < delays.Length; i++)
            {
                _ = poContext.ScheduledBlobs.Add(
                    new ScheduledBlob
                    {
                        ContainerName = containerName,
                        ChannelId = channelId,
                        Category = category,
                        Username = username + $", random {i + 1}/{delays.Length}",
                        ScheduledDate = DateTimeOffset.UtcNow.Add(delays[i]),
                    });
            }
            _ = await poContext.SaveChangesAsync();
            this.delays.ScheduledBlob.CancelDelay();

            return $"Scheduled {countRequested} images randomly over the next {duration.parsed}.";
        }

        /// <summary>
        /// Schedules an image every <paramref name="intervalInput"/> for <paramref name="durationInput"/>, starting immediately.
        /// </summary>
        public async Task<string> ScheduleTimerAsync(string containerName, ulong channelId, string category, string username, string intervalInput, string durationInput)
        {
            using IServiceScope scope = this.serviceProvider.CreateScope();
            using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();

            string errorMessage = default;

            if (string.IsNullOrEmpty(intervalInput))
            {
                errorMessage = "`interval` must be supplied.";
            }
            (string msg, TimeSpan parsed) interval = DurationParser.Parse(intervalInput);
            if (interval.msg != default)
            {
                errorMessage = interval.msg;
            }
            else if (interval.parsed < TimeSpan.FromSeconds(30) || interval.parsed > TimeSpan.FromDays(3))
            {
                errorMessage = $"Interval must be between 30 seconds and 3 days. (Is currently `{interval.parsed}`)";
            }

            if (string.IsNullOrEmpty(durationInput))
            {
                errorMessage = "`duration` must be supplied.";
            }
            (string msg, TimeSpan parsed) duration = DurationParser.Parse(durationInput);
            if (duration.msg != default)
            {
                errorMessage = duration.msg;
            }
            else if (duration.parsed > TimeSpan.FromDays(30))
            {
                errorMessage = $"Duration cannot last more than 30 days. (Is currently `{duration.parsed}`)";
            }

            int countRequested = (int)(duration.parsed.TotalSeconds / interval.parsed.TotalSeconds) + 1;
            int countAvailable = await poContext.Blobs.CountAsync(x => x.ContainerName == containerName && x.Category.StartsWith(category ?? string.Empty) && !x.Seen);
            if (countRequested > countAvailable * 1.1)
            {
                errorMessage = $"Requested to schedule {countRequested}, but only {countAvailable} are available (including a 10% buffer). Request fewer scheduled images.";
            }

            if (errorMessage != default)
            {
                return errorMessage;
            }

            for (int i = 0; i < countRequested; i++)
            {
                _ = poContext.ScheduledBlobs.Add(
                    new ScheduledBlob
                    {
                        ContainerName = containerName,
                        ChannelId = channelId,
                        Category = category,
                        Username = username + $", timer {i + 1}/{countRequested}",
                        ScheduledDate = DateTimeOffset.UtcNow.AddSeconds(interval.parsed.TotalSeconds * i),
                    });
            }
            _ = await poContext.SaveChangesAsync();
            this.delays.ScheduledBlob.CancelDelay();

            return $"Scheduled {countRequested} images every {interval.parsed} for the next {duration.parsed}.";
        }
    }
}
