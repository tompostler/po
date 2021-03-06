using Discord;
using Microsoft.ApplicationInsights;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    public sealed class CleanUpOldMessagesBackgroundService : SqlSynchronizedBackgroundService
    {
        public CleanUpOldMessagesBackgroundService(
            IServiceProvider serviceProvider,
            Sentinals sentinals,
            ILogger<CleanUpOldMessagesBackgroundService> logger,
            TelemetryClient telemetryClient)
            : base(serviceProvider, sentinals, logger, telemetryClient)
        { }

        protected override TimeSpan Interval => TimeSpan.FromDays(1);

        protected override async Task ExecuteOnceAsync(CancellationToken cancellationToken)
        {
            IEnumerable<ulong> channelIdsToNuke;
            using (IServiceScope scope = this.serviceProvider.CreateScope())
            using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
            {
                Models.SlashCommand nukeRegularlyCommand = await poContext.SlashCommands.Include(x => x.EnabledChannels).SingleOrDefaultAsync(x => x.Name == "nuke-messages-older-than-day", cancellationToken);
                channelIdsToNuke = nukeRegularlyCommand?.EnabledChannels.Select(x => x.ChannelId) ?? Enumerable.Empty<ulong>();
            }

            Discord.WebSocket.DiscordSocketClient discordClient = await this.sentinals.DiscordClient.WaitForCompletionAsync(cancellationToken);
            foreach (ulong channelId in channelIdsToNuke)
            {
                if (await discordClient.GetChannelAsync(channelId, cancellationToken.ToRO()) is not IMessageChannel channel)
                {
                    this.logger.LogWarning($"Could not get channel {channelId} as a message channel for cleanup.");
                    continue;
                }

                DateTimeOffset dayAgo = DateTimeOffset.UtcNow.AddDays(-1);
                uint count = 0;
                foreach (IMessage message in await channel.GetMessagesAsync(options: cancellationToken.ToRO()).FlattenAsync())
                {
                    if (message.Timestamp < dayAgo)
                    {
                        await channel.DeleteMessageAsync(message, cancellationToken.ToRO());
                        count++;
                    }
                }
                if (count > 1)
                {
                    _ = await channel.SendMessageAsync($"Background service: Purged {count} messages in channel older than {dayAgo:u}.", options: cancellationToken.ToRO());
                }
            }
        }
    }
}
