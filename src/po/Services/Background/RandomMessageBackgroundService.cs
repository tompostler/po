﻿using Discord;
using Discord.WebSocket;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.EntityFrameworkCore;
using po.DataAccess;
using po.Extensions;
using po.Utilities;

namespace po.Services.Background
{
    public sealed class RandomMessageBackgroundService : BackgroundService
    {
        private readonly IServiceProvider serviceProvider;
        private readonly Sentinals sentinals;
        private readonly ILogger<RandomMessageBackgroundService> logger;
        private readonly TelemetryClient telemetryClient;

        public RandomMessageBackgroundService(
            IServiceProvider serviceProvider,
            Sentinals sentinals,
            ILogger<RandomMessageBackgroundService> logger,
            TelemetryClient telemetryClient)
        {
            this.serviceProvider = serviceProvider;
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
                        Models.RandomMessage randomMessage = await poContext.RandomMessages.OrderBy(x => x.Id).FirstOrDefaultAsync(stoppingToken);

                        if (randomMessage != default)
                        {
                            DiscordSocketClient discordClient = await this.sentinals.DiscordClient.WaitForCompletionAsync(stoppingToken);

                            Color? color = default;
                            if (randomMessage.Title.Contains("not successful") || randomMessage.Title.Contains("unsuccessful"))
                            {
                                color = Color.Red;
                            }
                            else if (randomMessage.Title.Contains("successful"))
                            {
                                color = Color.Green;
                            }

                            EmbedBuilder embedBuilder = new()
                            {
                                Color = color,
                                Description = (randomMessage.Description + $"\n\n(random message {randomMessage.Id})").Trim(),
                                Title = randomMessage.Title,
                                Timestamp = randomMessage.CreatedDate
                            };

                            await discordClient.SendEmbedMessageAsync(
                                randomMessage.ChannelId,
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
                    // but less than an hour
                    delay = TimeSpan.FromHours(Math.Min(1, delay.TotalHours));
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
