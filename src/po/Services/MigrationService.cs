using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using po.DataAccess;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace po.Services
{
    public sealed class MigrationService : IHostedService
    {
        private readonly MigrationInitCompletionSignal signal;
        private readonly IServiceProvider serviceProvider;
        private readonly ILogger<MigrationService> logger;

        public MigrationService(
            MigrationInitCompletionSignal signal,
            IServiceProvider serviceProvider,
            ILogger<MigrationService> logger)
        {
            this.signal = signal;
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
            this.signal.SignalCompletion();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
