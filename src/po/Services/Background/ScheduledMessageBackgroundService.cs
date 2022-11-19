using Discord;
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
    public sealed class ScheduledMessageBackgroundService : BackgroundService
    {
        private readonly IServiceProvider serviceProvider;
        private readonly Delays delays;
        private readonly Sentinals sentinals;
        private readonly ILogger<ScheduledMessageBackgroundService> logger;
        private readonly TelemetryClient telemetryClient;

        public ScheduledMessageBackgroundService(
            IServiceProvider serviceProvider,
            Delays delays,
            Sentinals sentinals,
            ILogger<ScheduledMessageBackgroundService> logger,
            TelemetryClient telemetryClient)
        {
            this.serviceProvider = serviceProvider;
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
                        List<Models.ScheduledMessage> nextScheduledMessages = await poContext.ScheduledMessages
                                                                                .OrderBy(x => x.ScheduledDate)
                                                                                .Take(2)
                                                                                .ToListAsync(stoppingToken);
                        Models.ScheduledMessage nextScheduledMessage = nextScheduledMessages.FirstOrDefault();

                        if (nextScheduledMessage?.ScheduledDate < DateTimeOffset.UtcNow)
                        {
                            DiscordSocketClient discordClient = await this.sentinals.DiscordClient.WaitForCompletionAsync(stoppingToken);
                            var channel = discordClient.GetChannel(nextScheduledMessage.ChannelId) as SocketTextChannel;

                            TimeSpan? nextMessageIn = default;
                            if (nextScheduledMessages.Count == 2)
                            {
                                nextMessageIn = nextScheduledMessages.Last().ScheduledDate - nextScheduledMessage.ScheduledDate;
                            }

                            _ = await channel.SendMessageAsync(
                                embed: new EmbedBuilder()
                                {
                                    Author = new EmbedAuthorBuilder()
                                    {
                                        Name = nextScheduledMessage.Author
                                    },
                                    Description = nextScheduledMessage.Message,
                                    Timestamp = nextScheduledMessage.CreatedDate
                                }.Build(),
                                options: stoppingToken.ToRO());

                            _ = poContext.ScheduledMessages.Remove(nextScheduledMessage);
                            _ = await poContext.SaveChangesAsync(stoppingToken);
                            delay = TimeSpan.FromMinutes(1);
                        }
                        else if (nextScheduledMessage != default)
                        {
                            // If there's no current message to send, let the delay be until the next message
                            delay = nextScheduledMessage.ScheduledDate.Subtract(DateTimeOffset.UtcNow);
                        }
                    }
                    // The delay should be at least 1 minute
                    delay = TimeSpan.FromMinutes(Math.Max(1, delay.TotalMinutes));
                    // But less than a day
                    delay = TimeSpan.FromDays(Math.Min(1, delay.TotalDays));
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, "Could not check/handle scheduled message.");
                }

                await this.delays.ScheduledMessage.Delay(delay, this.logger, stoppingToken);
            }
        }
    }
}
