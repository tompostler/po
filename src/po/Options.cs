using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace po
{
    public static class Options
    {
        public static IServiceCollection AddPoConfig(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<Discord>(configuration.GetSection(nameof(Discord)));
            services.Configure<Sql>(configuration.GetSection(nameof(Sql)));
            return services;
        }

        public sealed class Discord
        {
            public string BotToken { get; set; }
        }
        public sealed class Sql
        {
            public string ConnectionString { get; set; }
        }
    }
}
