namespace po.Chat
{
    /// <summary>
    /// A rich message, such as a Discord embed or a Mattermost attachment, independent of the platform that will send it.
    /// Every property is optional; each platform renders the ones that are set.
    /// </summary>
    public sealed record ChatMessage
    {
        public string Title { get; init; }

        public string Text { get; init; }

        /// <summary>
        /// CSS color string in hex in the #RGB, #RGBA, #RRGGBB, or #RRGGBBAA format.
        /// </summary>
        public string ColorHex { get; init; }

        public Uri ImageUrl { get; init; }

        public string AuthorName { get; init; }

        public string Footer { get; init; }
    }
}
