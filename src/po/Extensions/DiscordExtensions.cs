using Discord;
using Microsoft.EntityFrameworkCore;
using po.DataAccess;
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
            IServiceProvider serviceProvider,
            IPoStorage poStorage,
            string containerName,
            string category,
            string username,
            Func<string, Task> textMessageResponse,
            Func<Embed, Task> embedMessageResponse,
            TimeSpan? nextImageIn = default)
        {
            using IServiceScope scope = serviceProvider.CreateScope();
            using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();

            Models.PoBlob blob = await poContext.Blobs
                            .Where(x => x.ContainerName == containerName && x.Category.StartsWith(category ?? string.Empty) && !x.Seen)
                            .OrderBy(x => Guid.NewGuid())
                            .FirstOrDefaultAsync();

            if (blob == default)
            {
                await textMessageResponse($"Category prefix `{category ?? "(any)"}` has no images remaining. Try `/po reset category`");
            }
            else
            {
                var counts = await poContext.Blobs
                            .Where(x => x.ContainerName == containerName && x.Category.StartsWith(category ?? string.Empty) && !x.Seen)
                            .GroupBy(x => x.Category)
                            .Select(g => new
                            {
                                g.Key,
                                CountUnseen = g.Count()
                            })
                            .ToListAsync();
                double chance = 1.0 * counts.Single(c => c.Key == blob.Category).CountUnseen / counts.Sum(c => c.CountUnseen);

                string nextImageInText = string.Empty;
                if (nextImageIn != default)
                {
                    nextImageInText = "\nNext image in ";
                    if (nextImageIn.Value.TotalHours > 1)
                    {
                        nextImageInText += nextImageIn.Value.TotalHours.ToString("0.0");
                        nextImageInText += 'h';
                    }
                    else
                    {
                        nextImageInText += nextImageIn.Value.TotalMinutes.ToString("0.0");
                        nextImageInText += 'm';
                    }
                }

                var builder = new EmbedBuilder()
                {
                    Title = blob.Name,
                    Description = $"Request: `{category ?? "(any)"}` ({username})\nResponse category chance: {chance:P2}{nextImageInText}",
                    ImageUrl = poStorage.GetReadOnlyUri(blob).AbsoluteUri
                };
                await embedMessageResponse(builder.Build());

                blob.Seen = true;
                _ = await poContext.SaveChangesAsync();
            }
        }
    }
}
