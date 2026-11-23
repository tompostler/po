using po.Chat;
using po.Commands;

namespace po.Platforms.Mattermost
{
    /// <summary>
    /// A <see cref="CommandContext"/> over a Mattermost slash command request.
    /// In-channel replies are posts by the bot, and ephemeral replies go through the request's <c>response_url</c>.
    /// </summary>
    /// <remarks>
    /// Ephemeral replies avoid <c>posts/ephemeral</c>, because its permission is granted only to system admins.
    /// </remarks>
    public sealed class MattermostCommandContext : CommandContext
    {
        private readonly MattermostCommandRequest request;
        private readonly ParsedArguments arguments;
        private readonly MattermostPlatform platform;
        private readonly MattermostApiClient apiClient;

        public MattermostCommandContext(
            MattermostCommandRequest request,
            ParsedArguments arguments,
            MattermostPlatform platform,
            MattermostApiClient apiClient,
            CancellationToken cancellationToken)
        {
            this.request = request;
            this.arguments = arguments;
            this.platform = platform;
            this.apiClient = apiClient;
            this.CancellationToken = cancellationToken;
            this.Channel = new ChannelRef(MattermostPlatform.PlatformName, request.ChannelId);
        }

        public override ChannelRef Channel { get; }

        public override string UserName => this.request.UserName;

        public override string Subcommand => this.arguments.Subcommand;

        public override int MaxMessageLength => this.platform.MaxMessageLength;

        public override CancellationToken CancellationToken { get; }

        public override Task ReplyAsync(ChatMessage message)
            => this.platform.SendMessageAsync(this.Channel, message, this.CancellationToken);

        public override Task ReplyWithFileAsync(string fileName, Stream content, string text)
            => this.platform.SendFileAsync(this.Channel, fileName, content, text, this.CancellationToken);

        protected override object GetOptionValue(string name)
            => this.arguments.Values.GetValueOrDefault(name);

        protected override Task ReplyTextAsync(string text, bool ephemeral)
            => ephemeral
                ? this.apiClient.PostCommandResponseAsync(
                    this.request.ResponseUrl,
                    new MattermostCommandResponse { ResponseType = MattermostCommandResponse.Ephemeral, Text = text },
                    this.CancellationToken)
                : this.platform.SendTextAsync(this.Channel, text, this.CancellationToken);
    }
}
