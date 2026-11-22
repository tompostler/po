using Discord;
using System.Text;

namespace po.Extensions
{
    public static class DiscordExtensions
    {
        public static RequestOptions ToRO(this CancellationToken @this) => new() { CancelToken = @this };

        public static async Task TrySendNotificationTextMessageOrFileAsync(this Discord.WebSocket.DiscordSocketClient @this, Options.Discord options, string message, ILogger logger, CancellationToken cancellationToken)
        {
            logger.LogInformation($"Attempting to send notification message to {options.BotPrimaryGuildId}/{options.BotNotificationChannelId}");
            try
            {
                Discord.WebSocket.SocketTextChannel channel = @this.GetGuild(options.BotPrimaryGuildId).GetTextChannel(options.BotNotificationChannelId);
                if (message.Length > 2000)
                {
                    var fileStream = new MemoryStream(Encoding.UTF8.GetBytes(message.Trim().Trim('`')));
                    _ = await channel.SendFileAsync(fileStream, "update.txt", "The background update was too long for a message, so it has been posted as a file.", options: cancellationToken.ToRO());
                }
                else
                {
                    _ = await channel.SendMessageAsync(message, options: cancellationToken.ToRO());
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Could not send notification message: {ex}");
            }
        }

        public static async Task SendTextMessageAsync(this Discord.WebSocket.DiscordSocketClient @this, ulong channelId, string message, ILogger logger, CancellationToken cancellationToken)
        {
            logger.LogInformation($"Attempting to send text message to {channelId}");
            var channel = await @this.GetChannelAsync(channelId, cancellationToken.ToRO()) as Discord.WebSocket.SocketTextChannel;
            _ = await channel.SendMessageAsync(message, options: cancellationToken.ToRO());
        }

        public static async Task SendEmbedMessageAsync(this Discord.WebSocket.DiscordSocketClient @this, ulong channelId, Embed embed, ILogger logger, CancellationToken cancellationToken)
        {
            logger.LogInformation($"Attempting to send embed message to {channelId}");
            var channel = await @this.GetChannelAsync(channelId, cancellationToken.ToRO()) as Discord.WebSocket.SocketTextChannel;
            _ = await channel.SendMessageAsync(embed: embed, options: cancellationToken.ToRO());
        }

        public static async Task SendSingleImageAsync(
            Services.Po.PoImageService poImageService,
            string containerName,
            string category,
            string username,
            Func<string, Task> textMessageResponse,
            Func<Embed, Task> embedMessageResponse,
            TimeSpan? nextImageIn = default)
        {
            Services.Po.PoImage image = await poImageService.PickUnseenAsync(containerName, category, username, nextImageIn);

            if (image == default)
            {
                await textMessageResponse(Services.Po.PoImageService.NoneRemainingMessage(category));
            }
            else
            {
                await embedMessageResponse(Platforms.Discord.DiscordPlatform.ToEmbed(image.ToChatMessage()));

                await poImageService.MarkSeenAsync(image);
            }
        }
    }
}
