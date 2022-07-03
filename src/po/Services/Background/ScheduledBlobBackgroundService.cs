using Discord.WebSocket;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using po.DataAccess;
using po.Extensions;
using po.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace po.Services.Background
{
    public sealed class ScheduledBlobBackgroundService : BackgroundService
    {
        private readonly IServiceProvider serviceProvider;
        private readonly PoStorage poStorage;
        private readonly Sentinals sentinals;
        private readonly ILogger<ScheduledBlobBackgroundService> logger;
        private readonly TelemetryClient telemetryClient;

        public ScheduledBlobBackgroundService(
            IServiceProvider serviceProvider,
            PoStorage poStorage,
            Sentinals sentinals,
            ILogger<ScheduledBlobBackgroundService> logger,
            TelemetryClient telemetryClient)
        {
            this.serviceProvider = serviceProvider;
            this.poStorage = poStorage;
            this.sentinals = sentinals;
            this.logger = logger;
            this.telemetryClient = telemetryClient;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await this.sentinals.DBMigration.WaitForCompletionAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                using IOperationHolder<RequestTelemetry> op = this.telemetryClient.StartOperation<RequestTelemetry>(this.GetType().FullName);
                var delay = TimeSpan.FromMinutes(1);
                try
                {
                    using (IServiceScope scope = this.serviceProvider.CreateScope())
                    using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
                    {
                        List<Models.ScheduledBlob> nextScheduledBlobs = await poContext.ScheduledBlobs
                                                                    .OrderBy(x => x.ScheduledDate)
                                                                    .Take(2)
                                                                    .ToListAsync(stoppingToken);
                        Models.ScheduledBlob nextScheduledBlob = nextScheduledBlobs.FirstOrDefault();

                        if (nextScheduledBlob?.ScheduledDate < DateTimeOffset.UtcNow)
                        {
                            DiscordSocketClient discordClient = await this.sentinals.DiscordClient.WaitForCompletionAsync(stoppingToken);
                            var channel = discordClient.GetChannel(nextScheduledBlob.ChannelId) as SocketTextChannel;

                            TimeSpan? nextImageIn = default;
                            if (nextScheduledBlobs.Count == 2)
                            {
                                nextImageIn = nextScheduledBlobs.Last().ScheduledDate - nextScheduledBlob.ScheduledDate;
                            }

                            await DiscordExtensions.SendSingleImageAsync(
                                this.serviceProvider,
                                this.poStorage,
                                nextScheduledBlob.ContainerName,
                                nextScheduledBlob.Category,
                                nextScheduledBlob.Username,
                                (message) => channel.SendMessageAsync(message),
                                (embed) => channel.SendMessageAsync(embed: embed),
                                nextImageIn);

                            _ = poContext.ScheduledBlobs.Remove(nextScheduledBlob);
                            _ = await poContext.SaveChangesAsync(stoppingToken);
                        }
                        else if (nextScheduledBlob != default)
                        {
                            delay = nextScheduledBlob.ScheduledDate.Subtract(DateTimeOffset.UtcNow);
                        }
                    }
                    delay = TimeSpan.FromMinutes(Math.Max(1, delay.TotalMinutes / 2));
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, "Could not check/handle scheduled image.");
                }

                this.logger.LogInformation($"Sleeping {delay} until the next iteration.");
                await Task.Delay(delay, stoppingToken);
            }
        }
    }
}
