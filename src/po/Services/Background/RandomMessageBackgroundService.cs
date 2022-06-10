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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace po.Services.Background
{
    public sealed class RandomMessageBackgroundService : BackgroundService
    {
        private readonly IServiceProvider serviceProvider;
        private readonly PoStorage poStorage;
        private readonly Sentinals sentinals;
        private readonly ILogger<RandomMessageBackgroundService> logger;
        private readonly TelemetryClient telemetryClient;

        public RandomMessageBackgroundService(
            IServiceProvider serviceProvider,
            PoStorage poStorage,
            Sentinals sentinals,
            ILogger<RandomMessageBackgroundService> logger,
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

            var delay = TimeSpan.FromMinutes(1);
            while (!stoppingToken.IsCancellationRequested)
            {
                using IOperationHolder<RequestTelemetry> op = this.telemetryClient.StartOperation<RequestTelemetry>(this.GetType().FullName);
                try
                {
                    using (IServiceScope scope = this.serviceProvider.CreateScope())
                    using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
                    {
                        Models.RandomMessage randomMessage = await poContext.RandomMessages.FirstOrDefaultAsync(stoppingToken);

                        if (randomMessage != default)
                        {
                            DiscordSocketClient discordClient = await this.sentinals.DiscordClient.WaitForCompletionAsync(stoppingToken);

                            EmbedBuilder embedBuilder = new()
                            {
                                Description = $"Random message {randomMessage.Id}\nCreated {randomMessage.CreatedDate:u}",
                            };

                            await discordClient.SendTextMessageAsync(
                                randomMessage.ChannelId,
                                randomMessage.Message,
                                embedBuilder.Build(),
                                this.logger,
                                stoppingToken);

                            _ = poContext.RandomMessages.Remove(randomMessage);
                            _ = await poContext.SaveChangesAsync(stoppingToken);

                            // Reset the delay
                            delay = TimeSpan.Zero;
                        }
                        else
                        {
                            // Exponential backoff sleep
                            delay *= 2;
                        }
                    }
                    // The delay should be at least 1 minute
                    delay = TimeSpan.FromMinutes(Math.Max(1, delay.TotalMinutes));
                    // but less than int.MaxValue milliseconds
                    delay = TimeSpan.FromMilliseconds(Math.Min(int.MaxValue - 1, delay.TotalMilliseconds));
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, "Could not send message.");
                }

                this.logger.LogInformation($"Sleeping {delay} until the next iteration.");
                await Task.Delay(delay, stoppingToken);
            }
        }
    }
}
