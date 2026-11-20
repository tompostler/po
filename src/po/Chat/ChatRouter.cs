using System.Text;

namespace po.Chat
{
    /// <summary>
    /// Sends to a <see cref="ChannelRef"/> through the <see cref="IChatPlatform"/> that owns it.
    /// </summary>
    public sealed class ChatRouter
    {
        private readonly Dictionary<string, IChatPlatform> platforms;

        public ChatRouter(IEnumerable<IChatPlatform> platforms)
        {
            this.platforms = platforms.ToDictionary(x => x.Name);
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
