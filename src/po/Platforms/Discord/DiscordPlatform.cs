using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using po.Chat;
using po.Extensions;

namespace po.Platforms.Discord
{
    /// <summary>
    /// Owns the Discord socket client's login and lifetime, and sends to Discord through it.
    /// </summary>
    public sealed class DiscordPlatform : IChatPlatform, IHostedService
    {
        public const string PlatformName = "discord";

        private readonly Options.Discord options;
        private readonly ILogger<DiscordPlatform> logger;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DiscordPlatform(
            IOptions<Options.Discord> options,
            ILogger<DiscordPlatform> logger)
        {
            this.options = options.Value;
            this.logger = logger;

            this.Client = new DiscordSocketClient();
            this.Client.Log += this.Client_Log;
            this.Client.Ready += this.Client_Ready;
        }

        /// <summary>
        /// The socket client, for <see cref="Services.BotService"/> to handle commands until they move behind the command abstraction.
        /// Wait for <see cref="WaitUntilReadyAsync"/> before using anything that needs the guild cache.
        /// </summary>
        public DiscordSocketClient Client { get; }

        /// <summary>
        /// The <see cref="ChannelRef"/> for a Discord channel id, such as one from an interaction payload.
        /// </summary>
        public static ChannelRef ToChannelRef(ulong channelId) => new(PlatformName, channelId.ToString());

        public string Name => PlatformName;

        public int MaxMessageLength => 2000;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            string botToken = this.options.BotToken;
            this.logger.LogInformation($"Token is {botToken?.Length.ToString() ?? "<null>"} characters.");

            await this.Client.LoginAsync(TokenType.Bot, botToken);
            await this.Client.StartAsync();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await this.Client.LogoutAsync();
            await this.Client.StopAsync();
        }

        /// <remarks>
        /// Ready fires again after every reconnect, but only the first one matters here.
        /// The handler must not wait on anything that sends, because Discord.Net runs it on the gateway task.
        /// </remarks>
        private Task Client_Ready()
        {
            _ = this.ready.TrySetResult();
            return Task.CompletedTask;
        }

        private Task Client_Log(LogMessage arg)
        {
            LogLevel logLevel = arg.Severity switch
            {
                LogSeverity.Critical => LogLevel.Critical,
                LogSeverity.Error => LogLevel.Error,
                LogSeverity.Warning => LogLevel.Warning,
                LogSeverity.Info => LogLevel.Information,
                LogSeverity.Verbose => LogLevel.Trace,
                LogSeverity.Debug => LogLevel.Debug,
                _ => LogLevel.Information
            };

            this.logger.Log(logLevel, arg.ToString(prependTimestamp: false));

            return Task.CompletedTask;
        }

        public Task WaitUntilReadyAsync(CancellationToken cancellationToken)
            => this.ready.Task.WaitAsync(cancellationToken);

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

        /// <remarks>
        /// Not supported, because the Discord nuke commands use Discord.Net directly and are removed with Discord.
        /// </remarks>
        public Task<IReadOnlyList<ChannelMessage>> ListMessagesAsync(ChannelRef channel, int limit, CancellationToken cancellationToken)
            => throw new NotSupportedException($"{PlatformName} doesn't list messages through {nameof(IChatPlatform)}.");

        /// <inheritdoc cref="ListMessagesAsync"/>
        public Task DeleteMessageAsync(ChannelRef channel, string messageId, CancellationToken cancellationToken)
            => throw new NotSupportedException($"{PlatformName} doesn't delete messages through {nameof(IChatPlatform)}.");

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

            await this.WaitUntilReadyAsync(cancellationToken);

            this.logger.LogInformation($"Attempting to send to {channel}");
            return await this.Client.GetChannelAsync(ulong.Parse(channel.Id), cancellationToken.ToRO()) as IMessageChannel
                ?? throw new InvalidOperationException($"{channel} was not found or is not a message channel.");
        }
    }
}
