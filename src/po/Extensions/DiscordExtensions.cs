using Discord;
using System.Threading;

namespace po.Extensions
{
    public static class DiscordExtensions
    {
        public static RequestOptions ToRO(this CancellationToken @this) => new() { CancelToken = @this };
    }
}
