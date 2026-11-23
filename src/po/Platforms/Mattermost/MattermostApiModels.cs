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
    /// A page of a channel's posts: <see cref="Order"/> holds their ids newest first, and <see cref="Posts"/> holds the posts by id.
    /// </summary>
    public sealed record MattermostPostList(IReadOnlyList<string> Order, IReadOnlyDictionary<string, MattermostListedPost> Posts);

    /// <summary>
    /// A post as listed, which is read rather than sent, so its props stay raw: other integrations' props can have any shape.
    /// </summary>
    /// <param name="CreateAt">Unix time in milliseconds.</param>
    /// <param name="Type">Empty for an ordinary post, or a value such as <c>system_join_channel</c> for one Mattermost made itself.</param>
    public sealed record MattermostListedPost(string Id, long CreateAt, string Type, IReadOnlyDictionary<string, System.Text.Json.JsonElement> Props);

    /// <summary>
    /// A message attachment: a card with a colored edge, a title, text and an optional image.
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

    public sealed record MattermostTeam(string Id, string Name);

    /// <param name="DeleteAt">Unix time in milliseconds when the channel was archived, or 0 if it's active.</param>
    public sealed record MattermostChannel(string Id, string Name, long DeleteAt);

    /// <summary>
    /// A custom slash command registered in a team.
    /// </summary>
    public sealed record MattermostCommand
    {
        public string Id { get; init; }

        /// <summary>
        /// Sent with every request for this command, so the receiver can check that it came from Mattermost.
        /// </summary>
        public string Token { get; init; }

        public string TeamId { get; init; }

        public string CreatorId { get; init; }

        /// <summary>
        /// The word after the slash, without the slash.
        /// </summary>
        public string Trigger { get; init; }

        /// <summary>
        /// <c>P</c> to POST the request to <see cref="Url"/>, or <c>G</c> to GET it.
        /// </summary>
        public string Method { get; init; }

        public string Url { get; init; }

        public string DisplayName { get; init; }

        public string Description { get; init; }

        public bool AutoComplete { get; init; }

        public string AutoCompleteDesc { get; init; }

        public string AutoCompleteHint { get; init; }
    }

    public sealed record MattermostTokenResponse(string Token);

    /// <summary>
    /// A reply to a slash command, either as the HTTP response to Mattermost's request or posted to its <c>response_url</c>.
    /// </summary>
    public sealed record MattermostCommandResponse
    {
        public const string Ephemeral = "ephemeral";

        /// <summary>
        /// <see cref="Ephemeral"/> for a reply only the invoking user sees, or <c>in_channel</c> for a regular post.
        /// </summary>
        public string ResponseType { get; init; }

        public string Text { get; init; }
    }
}
