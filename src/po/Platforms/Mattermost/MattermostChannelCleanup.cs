using Microsoft.EntityFrameworkCore;
using po.Chat;
using po.DataAccess;
using po.Utilities;

namespace po.Platforms.Mattermost
{
    /// <summary>
    /// Once at startup, removes the enablement rows and scheduled items of Mattermost channels that were archived or deleted, and says so in the notification channels.
    /// </summary>
    /// <remarks>
    /// Scheduled items matter most: <see cref="Services.Background.ScheduledBlobBackgroundService"/> always takes the earliest row, so one for a dead channel fails forever and holds up every image scheduled after it.
    /// A channel the bot can't see (403) is left alone, because that's a membership problem rather than a dead channel.
    /// </remarks>
    public sealed class MattermostChannelCleanup : BackgroundService
    {
        private readonly IServiceProvider serviceProvider;
        private readonly MattermostPlatform platform;
        private readonly MattermostApiClient apiClient;
        private readonly ChatRouter chatRouter;
        private readonly Sentinels sentinels;
        private readonly ILogger<MattermostChannelCleanup> logger;

        public MattermostChannelCleanup(
            IServiceProvider serviceProvider,
            MattermostPlatform platform,
            MattermostApiClient apiClient,
            ChatRouter chatRouter,
            Sentinels sentinels,
            ILogger<MattermostChannelCleanup> logger)
        {
            this.serviceProvider = serviceProvider;
            this.platform = platform;
            this.apiClient = apiClient;
            this.chatRouter = chatRouter;
            this.sentinels = sentinels;
            this.logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await this.sentinels.DBMigration.WaitForCompletionAsync(stoppingToken);
                await this.platform.WaitUntilReadyAsync(stoppingToken);
                await this.CleanUpAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // An unhandled exception here would stop the host.
                this.logger.LogError(ex, "Could not clean up archived or deleted Mattermost channels.");
            }
        }

        private async Task CleanUpAsync(CancellationToken cancellationToken)
        {
            const string Platform = MattermostPlatform.PlatformName;
            using IServiceScope scope = this.serviceProvider.CreateScope();
            using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();

            List<string> channelIds = await poContext.CommandChannels.Where(x => x.Platform == Platform).Select(x => x.ChannelId)
                .Union(poContext.ScheduledBlobs.Where(x => x.Channel.Platform == Platform).Select(x => x.Channel.Id))
                .Union(poContext.ScheduledMessages.Where(x => x.Channel.Platform == Platform).Select(x => x.Channel.Id))
                .ToListAsync(cancellationToken);

            foreach (string channelId in channelIds)
            {
                MattermostChannel channel;
                try
                {
                    channel = await this.apiClient.GetChannelAsync(channelId, cancellationToken);
                }
                catch (HttpRequestException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    this.logger.LogWarning(ex, $"Could not check Mattermost channel {channelId}, so its rows are kept.");
                    continue;
                }

                if (channel != null && channel.DeleteAt == 0)
                {
                    continue;
                }

                var dead = new ChannelRef(Platform, channelId);
                int commandChannels = await poContext.CommandChannels.Where(x => x.Platform == Platform && x.ChannelId == channelId).ExecuteDeleteAsync(cancellationToken);
                int scheduledBlobs = await poContext.ScheduledBlobs.Where(x => x.Channel == dead).ExecuteDeleteAsync(cancellationToken);
                int scheduledMessages = await poContext.ScheduledMessages.Where(x => x.Channel == dead).ExecuteDeleteAsync(cancellationToken);

                string summary = $"Removed {dead} ({(channel == null ? "deleted" : $"archived, was ~{channel.Name}")}): {commandChannels} enabled command(s), {scheduledBlobs} scheduled image(s), {scheduledMessages} scheduled message(s).";
                this.logger.LogInformation(summary);
                await this.chatRouter.SendNotificationTextAsync(summary, cancellationToken);
            }
        }
    }
}
