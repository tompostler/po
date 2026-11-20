using Microsoft.EntityFrameworkCore;
using po.DataAccess;
using po.Utilities;

namespace po.Services
{
    public sealed class MigrationService : IHostedService
    {
        private readonly Sentinels sentinels;
        private readonly IServiceProvider serviceProvider;
        private readonly ILogger<MigrationService> logger;

        public MigrationService(
            Sentinels sentinels,
            IServiceProvider serviceProvider,
            ILogger<MigrationService> logger)
        {
            this.sentinels = sentinels;
            this.serviceProvider = serviceProvider;
            this.logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            this.logger.LogInformation("Applying migrations if necessary...");

            using IServiceScope scope = this.serviceProvider.CreateScope();
            using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();

            await poContext.Database.MigrateAsync(cancellationToken);

            this.logger.LogInformation("Migrations complete.");
            this.sentinels.DBMigration.SignalCompletion();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
