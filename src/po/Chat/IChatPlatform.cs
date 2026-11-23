namespace po.Chat
{
    /// <summary>
    /// Sends messages to channels on one chat platform, such as Discord or Mattermost.
    /// </summary>
    public interface IChatPlatform
    {
        /// <summary>
        /// The platform's name as used in <see cref="ChannelRef.Platform"/>, such as <c>discord</c>.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The longest text message the platform accepts, in characters.
        /// Longer text should be sent with <see cref="SendFileAsync"/> instead.
        /// </summary>
        int MaxMessageLength { get; }

        /// <summary>
        /// Completes once the platform is connected and able to send.
        /// </summary>
        Task WaitUntilReadyAsync(CancellationToken cancellationToken);

        Task SendTextAsync(ChannelRef channel, string text, CancellationToken cancellationToken);

        Task SendMessageAsync(ChannelRef channel, ChatMessage message, CancellationToken cancellationToken);

        /// <summary>
        /// Sends <paramref name="content"/> as an attached file named <paramref name="fileName"/>, with <paramref name="text"/> as the accompanying message.
        /// </summary>
        Task SendFileAsync(ChannelRef channel, string fileName, Stream content, string text, CancellationToken cancellationToken);

        /// <summary>
        /// Lists up to <paramref name="limit"/> of the channel's most recent messages, newest first, leaving out the platform's own system messages, such as join notices.
        /// </summary>
        Task<IReadOnlyList<ChannelMessage>> ListMessagesAsync(ChannelRef channel, int limit, CancellationToken cancellationToken);

        Task DeleteMessageAsync(ChannelRef channel, string messageId, CancellationToken cancellationToken);
    }
}
