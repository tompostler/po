using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using po.DataAccess;
using po.Utilities;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace po.Services
{
    public abstract class SqlSynchronizedBackgroundService : BackgroundService
    {
        protected readonly IServiceProvider serviceProvider;
        protected readonly Sentinals sentinals;
        protected readonly ILogger logger;

        public SqlSynchronizedBackgroundService(
            IServiceProvider serviceProvider,
            Sentinals sentinals,
            ILogger logger)
        {
            this.serviceProvider = serviceProvider;
            this.sentinals = sentinals;
            this.logger = logger;
        }

        protected override sealed async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await this.sentinals.DBMigration.WaitForCompletionAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                using (IServiceScope scope = this.serviceProvider.CreateScope())
                using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
                {
                    Models.SynchronizedBackgroundService lastExecution = await poContext.SynchronizedBackgroundServices.SingleOrDefaultAsync(x => x.Name == this.GetType().FullName, stoppingToken);
                    if (lastExecution != default && lastExecution.LastExecuted.Add(this.Interval) > DateTimeOffset.UtcNow)
                    {
                        this.logger.LogInformation($"Last execution of {this.GetType().FullName} was {DateTimeOffset.UtcNow - lastExecution.LastExecuted} ago when the interval is {this.Interval}.");
                        DateTimeOffset nextExpectedExecution = lastExecution.LastExecuted.Add(this.Interval);
                        TimeSpan ninetyPercentRemaining = nextExpectedExecution - DateTimeOffset.UtcNow;
                        var durationToSleep = TimeSpan.FromMinutes(Math.Max(5, ninetyPercentRemaining.TotalMinutes));
                        this.logger.LogInformation($"Determined we should sleep for {durationToSleep}");
                        await Task.Delay(durationToSleep, stoppingToken);
                        continue;
                    }

                    // Record that we've just started another loop
                    lastExecution ??= new Models.SynchronizedBackgroundService { Name = this.GetType().FullName };
                    lastExecution.LastExecuted = DateTimeOffset.UtcNow;
                    lastExecution.CountExecutions++;
                    _ = await poContext.SaveChangesAsync(stoppingToken);
                }

                // And then actually do the work
                try
                {
                    CancellationToken linkedToken = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, new CancellationTokenSource(this.Interval).Token).Token;
                    await this.ExecuteOnceAsync(linkedToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    this.logger.LogError(ex, "Failed execution.");
                }
            }
        }

        protected abstract TimeSpan Interval { get; }

        protected abstract Task ExecuteOnceAsync(CancellationToken cancellationToken);
    }
}
