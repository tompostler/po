using Discord;
using Discord.WebSocket;
using po.Chat;
using po.Extensions;
using po.Utilities;

namespace po.Platforms.Discord
{
    /// <summary>
    /// Sends to Discord through the socket client that <see cref="Services.BotService"/> logs in.
    /// </summary>
    public sealed class DiscordPlatform : IChatPlatform
    {
        public const string PlatformName = "discord";

        private readonly Sentinels sentinels;
        private readonly ILogger<DiscordPlatform> logger;

        public DiscordPlatform(
            Sentinels sentinels,
            ILogger<DiscordPlatform> logger)
        {
            this.sentinels = sentinels;
            this.logger = logger;
        }

        public string Name => PlatformName;

        public int MaxMessageLength => 2000;

        public async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
            => _ = await this.sentinels.DiscordClient.WaitForCompletionAsync(cancellationToken);

        public async Task SendTextAsync(ChannelRef channel, string text, CancellationToken cancellationToken)
        {
            IMessageChannel messageChannel = await this.GetMessageChannelAsync(channel, cancellationToken);
            _ = await messageChannel.SendMessageAsync(text, options: cancellationToken.ToRO());
        }

        public async Task SendMessageAsync(ChannelRef channel, ChatMessage message, CancellationToken cancellationToken)
        {
            IMessageChannel messageChannel = await this.GetMessageChannelAsync(channel, cancellationToken);
            _ = await messageChannel.SendMessageAsync(embed: ToEmbed(message), options: cancellationToken.ToRO());
        }

        public async Task SendFileAsync(ChannelRef channel, string fileName, Stream content, string text, CancellationToken cancellationToken)
        {
            IMessageChannel messageChannel = await this.GetMessageChannelAsync(channel, cancellationToken);
            _ = await messageChannel.SendFileAsync(content, fileName, text, options: cancellationToken.ToRO());
        }

        public static Embed ToEmbed(ChatMessage message)
        {
            var builder = new EmbedBuilder
            {
                Title = message.Title,
                Description = message.Text,
                ImageUrl = message.ImageUrl?.AbsoluteUri,
            };

            if (Color.TryParse(message.ColorHex, out Color color))
            {
                builder.Color = color;
            }

            if (message.AuthorName != null)
            {
                builder.Author = new EmbedAuthorBuilder { Name = message.AuthorName };
            }

            if (message.Footer != null)
            {
                builder.Footer = new EmbedFooterBuilder { Text = message.Footer };
            }

            return builder.Build();
        }

        private async Task<IMessageChannel> GetMessageChannelAsync(ChannelRef channel, CancellationToken cancellationToken)
        {
            if (channel.Platform != PlatformName)
            {
                throw new ArgumentException($"{channel} is not a {PlatformName} channel.", nameof(channel));
            }

            DiscordSocketClient client = await this.sentinels.DiscordClient.WaitForCompletionAsync(cancellationToken);

            this.logger.LogInformation($"Attempting to send to {channel}");
            return await client.GetChannelAsync(ulong.Parse(channel.Id), cancellationToken.ToRO()) as IMessageChannel
                ?? throw new InvalidOperationException($"{channel} was not found or is not a message channel.");
        }
    }
}
