namespace po
{
    public static class Options
    {
        public static IServiceCollection AddPoConfig(this IServiceCollection services, IConfiguration configuration)
        {
            _ = services.Configure<Api>(configuration.GetSection(nameof(Api)));
            _ = services.Configure<Discord>(configuration.GetSection(nameof(Discord)));
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
