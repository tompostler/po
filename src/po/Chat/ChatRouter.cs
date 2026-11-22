using Microsoft.Extensions.Options;
using System.Text;

namespace po.Chat
{
    /// <summary>
    /// Sends to a <see cref="ChannelRef"/> through the <see cref="IChatPlatform"/> that owns it.
    /// </summary>
    public sealed class ChatRouter
    {
        private readonly Dictionary<string, IChatPlatform> platforms;
        private readonly List<ChannelRef> notificationChannels;
        private readonly ILogger<ChatRouter> logger;

        public ChatRouter(
            IEnumerable<IChatPlatform> platforms,
            IOptions<Options.Notifications> notificationOptions,
            ILogger<ChatRouter> logger)
        {
            this.platforms = platforms.ToDictionary(x => x.Name);
            this.notificationChannels = notificationOptions.Value.Channels.Select(ChannelRef.Parse).ToList();
            this.logger = logger;
        }

        public Task WaitUntilReadyAsync(string platformName, CancellationToken cancellationToken)
            => this.GetPlatform(platformName).WaitUntilReadyAsync(cancellationToken);

        /// <summary>
        /// Sends <paramref name="text"/> as a message, or as a file named <paramref name="fileName"/> with <paramref name="fileText"/> as the message if it's too long for the platform.
        /// Surrounding whitespace and code fences are trimmed from the file's content.
        /// </summary>
        public async Task SendTextAsync(
            ChannelRef channel,
            string text,
            CancellationToken cancellationToken,
            string fileName = "message.txt",
            string fileText = "The message was too long, so it has been posted as a file.")
        {
            IChatPlatform platform = this.GetPlatform(channel.Platform);
            if (text.Length > platform.MaxMessageLength)
            {
                using var content = new MemoryStream(Encoding.UTF8.GetBytes(text.Trim().Trim('`')));
                await platform.SendFileAsync(channel, fileName, content, fileText, cancellationToken);
            }
            else
            {
                await platform.SendTextAsync(channel, text, cancellationToken);
            }
        }

        /// <summary>
        /// Sends <paramref name="text"/> to every configured notification channel, like <see cref="SendTextAsync"/>.
        /// A failure on one channel is logged and doesn't stop the others or throw.
        /// </summary>
        public Task SendNotificationTextAsync(
            string text,
            CancellationToken cancellationToken,
            string fileName = "update.txt",
            string fileText = "The background update was too long for a message, so it has been posted as a file.")
            => this.SendToNotificationChannelsAsync(channel => this.SendTextAsync(channel, text, cancellationToken, fileName, fileText));

        /// <summary>
        /// Sends <paramref name="message"/> to every configured notification channel, like <see cref="SendMessageAsync"/>.
        /// A failure on one channel is logged and doesn't stop the others or throw.
        /// </summary>
        /// <returns>Whether at least one channel received the message, so a caller can keep it queued when none did.</returns>
        public Task<bool> SendNotificationMessageAsync(ChatMessage message, CancellationToken cancellationToken)
            => this.SendToNotificationChannelsAsync(channel => this.SendMessageAsync(channel, message, cancellationToken));

        private async Task<bool> SendToNotificationChannelsAsync(Func<ChannelRef, Task> send)
        {
            bool anySent = false;
            foreach (ChannelRef channel in this.notificationChannels)
            {
                this.logger.LogInformation($"Attempting to send notification message to {channel}");
                try
                {
                    await send(channel);
                    anySent = true;
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, $"Could not send notification message to {channel}");
                }
            }

            return anySent;
        }

        public Task SendMessageAsync(ChannelRef channel, ChatMessage message, CancellationToken cancellationToken)
            => this.GetPlatform(channel.Platform).SendMessageAsync(channel, message, cancellationToken);

        public Task SendFileAsync(ChannelRef channel, string fileName, Stream content, string text, CancellationToken cancellationToken)
            => this.GetPlatform(channel.Platform).SendFileAsync(channel, fileName, content, text, cancellationToken);

        private IChatPlatform GetPlatform(string platformName)
            => this.platforms.TryGetValue(platformName ?? string.Empty, out IChatPlatform platform)
                ? platform
                : throw new ArgumentException($"No chat platform named '{platformName}' is registered.", nameof(platformName));
    }
}
