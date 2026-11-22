using po.Chat;
using System.Net;

namespace po.Platforms.Mattermost
{
    /// <summary>
    /// Sends to Mattermost as the <c>po</c> bot through the REST API.
    /// Runs as a hosted service only to find out when the server first answers, because the bot and Mattermost containers start together.
    /// </summary>
    public sealed class MattermostPlatform : BackgroundService, IChatPlatform
    {
        public const string PlatformName = "mattermost";

        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(1);

        private readonly MattermostApiClient apiClient;
        private readonly ILogger<MattermostPlatform> logger;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MattermostPlatform(
            MattermostApiClient apiClient,
            ILogger<MattermostPlatform> logger)
        {
            this.apiClient = apiClient;
            this.logger = logger;
        }

        public string Name => PlatformName;

        /// <summary>
        /// The server's default <c>MaxPostSize</c>.
        /// </summary>
        public int MaxMessageLength => 16383;

        /// <summary>
        /// Retries <c>users/me</c> until the server answers.
        /// A rejected token is permanent, so it fails readiness instead of retrying, and sends fail fast rather than waiting forever.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            TimeSpan retryDelay = TimeSpan.FromSeconds(5);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    MattermostUser me = await this.apiClient.GetMeAsync(stoppingToken);
                    this.logger.LogInformation($"Connected to Mattermost as @{me.Username} ({me.Id}).");
                    _ = this.ready.TrySetResult();
                    return;
                }
                catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    this.logger.LogError(ex, "Mattermost rejected the bot token, so nothing will be sent to Mattermost.");
                    _ = this.ready.TrySetException(ex);
                    return;
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    this.logger.LogWarning(ex, $"Mattermost is not reachable yet. Retrying in {retryDelay}.");
                }

                await Task.Delay(retryDelay, stoppingToken);
                retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, MaxRetryDelay.Ticks));
            }
        }

        public Task WaitUntilReadyAsync(CancellationToken cancellationToken)
            => this.ready.Task.WaitAsync(cancellationToken);

        public async Task SendTextAsync(ChannelRef channel, string text, CancellationToken cancellationToken)
        {
            string channelId = await this.GetChannelIdAsync(channel, cancellationToken);
            _ = await this.apiClient.CreatePostAsync(new MattermostPost { ChannelId = channelId, Message = text }, cancellationToken);
        }

        public async Task SendMessageAsync(ChannelRef channel, ChatMessage message, CancellationToken cancellationToken)
        {
            string channelId = await this.GetChannelIdAsync(channel, cancellationToken);
            _ = await this.apiClient.CreatePostAsync(
                new MattermostPost
                {
                    ChannelId = channelId,
                    Props = new MattermostPostProps([ToAttachment(message)]),
                },
                cancellationToken);
        }

        public async Task SendFileAsync(ChannelRef channel, string fileName, Stream content, string text, CancellationToken cancellationToken)
        {
            string channelId = await this.GetChannelIdAsync(channel, cancellationToken);
            string fileId = await this.apiClient.UploadFileAsync(channelId, fileName, content, cancellationToken);
            _ = await this.apiClient.CreatePostAsync(
                new MattermostPost
                {
                    ChannelId = channelId,
                    Message = text ?? string.Empty,
                    FileIds = [fileId],
                },
                cancellationToken);
        }

        private static MattermostAttachment ToAttachment(ChatMessage message) => new()
        {
            // Shown in notifications and anywhere else the attachment can't render.
            Fallback = message.Title ?? message.Text ?? message.AuthorName ?? string.Empty,
            Color = message.ColorHex,
            Title = message.Title,
            Text = message.Text,
            AuthorName = message.AuthorName,
            ImageUrl = message.ImageUrl?.AbsoluteUri,
            Footer = message.Footer,
        };

        private async Task<string> GetChannelIdAsync(ChannelRef channel, CancellationToken cancellationToken)
        {
            if (channel.Platform != PlatformName)
            {
                throw new ArgumentException($"{channel} is not a {PlatformName} channel.", nameof(channel));
            }

            await this.WaitUntilReadyAsync(cancellationToken);

            this.logger.LogInformation($"Attempting to send to {channel}");
            return channel.Id;
        }
    }
}
