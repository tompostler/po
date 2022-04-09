using Discord;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace po.Extensions
{
    public static class DiscordExtensions
    {
        public static RequestOptions ToRO(this CancellationToken @this) => new() { CancelToken = @this };


        public static async Task TrySendNotificationTextMessageAsync(this Discord.WebSocket.DiscordSocketClient @this, Options.Discord options, string message, ILogger logger, CancellationToken cancellationToken)
        {
            logger.LogInformation($"Attempting to send notification message to {options.BotPrimaryGuildId}/{options.BotNotificationChannelId}");
            try
            {
                _ = await @this.GetGuild(options.BotPrimaryGuildId).GetTextChannel(options.BotNotificationChannelId).SendMessageAsync(message, options: cancellationToken.ToRO());
            }
            catch (Exception ex)
            {
                logger.LogError($"Could not send notification message: {ex}");
            }
        }
    }
}
