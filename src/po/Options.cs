namespace po
{
    public static class Options
    {
        public static IServiceCollection AddPoConfig(this IServiceCollection services, IConfiguration configuration)
        {
            _ = services.Configure<Api>(configuration.GetSection(nameof(Api)));
            _ = services.Configure<Discord>(configuration.GetSection(nameof(Discord)));
            _ = services.Configure<Mattermost>(configuration.GetSection(nameof(Mattermost)));
            _ = services.Configure<Notifications>(configuration.GetSection(nameof(Notifications)));
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
            public string BotToken { get; set; }
        }
        public sealed class Mattermost
        {
            /// <summary>
            /// The server's root, without <c>/api/v4</c>.
            /// Use the docker-network address, such as <c>http://mattermost:8065</c>, so API calls skip the public proxy.
            /// </summary>
            public Uri ApiBaseUri { get; set; }

            /// <summary>
            /// The access token of the <c>po</c> bot account.
            /// </summary>
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
