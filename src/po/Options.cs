using Microsoft.Extensions.Options;

namespace po
{
    public static class Options
    {
        public static IServiceCollection AddPoConfig(this IServiceCollection services, IConfiguration configuration)
        {
            _ = services.Configure<Api>(configuration.GetSection(nameof(Api)));
            _ = services.Configure<Discord>(configuration.GetSection(nameof(Discord)));
            _ = services.AddOptions<Notifications>()
                .Bind(configuration.GetSection(nameof(Notifications)))
                .PostConfigure<IOptions<Discord>>((notifications, discord) =>
                {
                    // Until notification channels are configured, keep notifying the Discord channel that has always been used.
                    if (notifications.Channels.Count == 0 && discord.Value.BotNotificationChannelId != 0)
                    {
                        notifications.Channels.Add(new Chat.ChannelRef(Platforms.Discord.DiscordPlatform.PlatformName, discord.Value.BotNotificationChannelId.ToString()).ToString());
                    }
                });
            _ = services.Configure<Sql>(configuration.GetSection(nameof(Sql)));
            _ = services.Configure<Storage>(configuration.GetSection(nameof(Storage)));
            return services;
        }

        public sealed class Api
        {
            public string ApiKey { get; set; }
        }

        public sealed class Discord
        {
            public ulong BotPrimaryGuildId { get; set; }
            public ulong BotNotificationChannelId { get; set; }
            public string BotToken { get; set; }
        }
        public sealed class Notifications
        {
            /// <summary>
            /// Channels that receive bot notifications, each in the <c>platform:id</c> form of <see cref="Chat.ChannelRef"/>.
            /// </summary>
            public List<string> Channels { get; set; } = [];
        }
        public sealed class Sql
        {
            public string ConnectionString { get; set; }
        }
        public sealed class Storage
        {
            public Uri BaseUri { get; set; }
        }
    }
}
