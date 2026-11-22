namespace po.Platforms.Mattermost
{
    // Request and response bodies for the Mattermost REST API v4, holding only the fields po uses.
    // Property names are serialized in snake_case by MattermostApiClient.

    public sealed record MattermostUser(string Id, string Username);

    public sealed record MattermostPost
    {
        public string Id { get; init; }

        public string ChannelId { get; init; }

        /// <summary>
        /// Required by the API even when the post is only attachments or files, so it defaults to empty rather than null, which serialization would omit.
        /// </summary>
        public string Message { get; init; } = string.Empty;

        public IReadOnlyList<string> FileIds { get; init; }

        public MattermostPostProps Props { get; init; }
    }

    public sealed record MattermostPostProps(IReadOnlyList<MattermostAttachment> Attachments);

    /// <summary>
    /// A message attachment, Mattermost's equivalent of a Discord embed.
    /// </summary>
    public sealed record MattermostAttachment
    {
        /// <summary>
        /// Plain text shown where the attachment can't be rendered, such as in notifications.
        /// </summary>
        public string Fallback { get; init; }

        public string Color { get; init; }

        public string Title { get; init; }

        public string Text { get; init; }

        public string AuthorName { get; init; }

        public string ImageUrl { get; init; }

        public string Footer { get; init; }
    }

    public sealed record MattermostFileInfo(string Id, string Name);

    public sealed record MattermostFileUploadResponse(IReadOnlyList<MattermostFileInfo> FileInfos);

    public sealed record MattermostError(string Id, string Message, int StatusCode);
}
