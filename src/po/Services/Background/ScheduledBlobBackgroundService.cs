﻿using Discord.WebSocket;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.EntityFrameworkCore;
using po.DataAccess;
using po.Extensions;
using po.Utilities;

namespace po.Services.Background
{
    public sealed class ScheduledBlobBackgroundService : BackgroundService
    {
        private readonly IServiceProvider serviceProvider;
        private readonly PoStorage poStorage;
        private readonly Delays delays;
        private readonly Sentinals sentinals;
        private readonly ILogger<ScheduledBlobBackgroundService> logger;
        private readonly TelemetryClient telemetryClient;

        public ScheduledBlobBackgroundService(
            IServiceProvider serviceProvider,
            PoStorage poStorage,
            Delays delays,
            Sentinals sentinals,
            ILogger<ScheduledBlobBackgroundService> logger,
            TelemetryClient telemetryClient)
        {
            this.serviceProvider = serviceProvider;
            this.poStorage = poStorage;
            this.delays = delays;
            this.sentinals = sentinals;
            this.logger = logger;
            this.telemetryClient = telemetryClient;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await this.sentinals.DBMigration.WaitForCompletionAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromDays(1);
                using IOperationHolder<RequestTelemetry> op = this.telemetryClient.StartOperation<RequestTelemetry>(this.GetType().FullName);
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

                            TimeSpan? nextBlobIn = default;
                            if (nextScheduledBlobs.Count == 2)
                            {
                                nextBlobIn = nextScheduledBlobs.Last().ScheduledDate - nextScheduledBlob.ScheduledDate;
                            }

                            await DiscordExtensions.SendSingleImageAsync(
                                this.serviceProvider,
                                this.poStorage,
                                nextScheduledBlob.ContainerName,
                                nextScheduledBlob.Category,
                                nextScheduledBlob.Username,
                                (message) => channel.SendMessageAsync(message),
                                (embed) => channel.SendMessageAsync(embed: embed),
                                nextBlobIn);

                            _ = poContext.ScheduledBlobs.Remove(nextScheduledBlob);
                            _ = await poContext.SaveChangesAsync(stoppingToken);
                            delay = TimeSpan.FromMinutes(1);
                        }
                        else if (nextScheduledBlob != default)
                        {
                            // If there's no current blob to show, let the delay be until the next blob
                            delay = nextScheduledBlob.ScheduledDate.Subtract(DateTimeOffset.UtcNow);
                        }
                    }
                    // The delay should be at least 1 minute
                    delay = TimeSpan.FromMinutes(Math.Max(1, delay.TotalMinutes));
                    // But less than a day
                    delay = TimeSpan.FromDays(Math.Min(1, delay.TotalDays));
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, "Could not check/handle scheduled image.");
                }

                await this.delays.ScheduledBlob.Delay(delay, this.logger, stoppingToken);
            }
        }
    }
}
