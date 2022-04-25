using Discord.WebSocket;
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
    public sealed class ScheduledBlobBackgroundService : BackgroundService
    {
        private readonly IServiceProvider serviceProvider;
        private readonly PoStorage poStorage;
        private readonly Sentinals sentinals;
        private readonly ILogger<ScheduledBlobBackgroundService> logger;

        public ScheduledBlobBackgroundService(
            IServiceProvider serviceProvider,
            PoStorage poStorage,
            Sentinals sentinals,
            ILogger<ScheduledBlobBackgroundService> logger)
        {
            this.serviceProvider = serviceProvider;
            this.poStorage = poStorage;
            this.sentinals = sentinals;
            this.logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await this.sentinals.DBMigration.WaitForCompletionAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromMinutes(1);
                using (IServiceScope scope = this.serviceProvider.CreateScope())
                using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
                {
                    Models.ScheduledBlob nextScheduledBlob = await poContext.ScheduledBlobs
                                                                .OrderBy(x => x.ScheduledDate)
                                                                .FirstOrDefaultAsync(stoppingToken);

                    if (nextScheduledBlob.ScheduledDate < DateTimeOffset.UtcNow)
                    {
                        DiscordSocketClient discordClient = await this.sentinals.DiscordClient.WaitForCompletionAsync(stoppingToken);
                        var channel = discordClient.GetChannel(nextScheduledBlob.ChannelId) as SocketTextChannel;

                        await DiscordExtensions.SendSingleImageAsync(
                            this.serviceProvider,
                            this.poStorage,
                            nextScheduledBlob.Category,
                            nextScheduledBlob.Username,
                            (message) => channel.SendMessageAsync(message),
                            (embed) => channel.SendMessageAsync(embed: embed));
                    }
                    else if (nextScheduledBlob != default)
                    {
                        delay = nextScheduledBlob.ScheduledDate.Subtract(DateTimeOffset.UtcNow);
                    }
                }
                delay = TimeSpan.FromMinutes(Math.Max(1, delay.TotalMinutes / 2));

                this.logger.LogInformation($"Sleeping {delay} until the next iteration.");
                await Task.Delay(delay, stoppingToken);
            }
        }
    }
}
