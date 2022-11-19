using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using po.DataAccess;
using po.Models;
using po.Utilities;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace po.DiscordImpl.SlashCommands
{
    public sealed class ScheduleMessage : SlashCommandBase
    {
        private readonly IServiceProvider serviceProvider;
        private readonly Delays delays;

        public ScheduleMessage(
            IServiceProvider serviceProvider,
            Delays delays)
        {
            this.serviceProvider = serviceProvider;
            this.delays = delays;
        }

        public override SlashCommand ExpectedCommand => new()
        {
            Name = "schedule-message",
            Version = 1,
            IsGuildLevel = true,
        };

        public override SlashCommandProperties BuiltCommand => new SlashCommandBuilder()
            .WithName(this.ExpectedCommand.Name)
            .WithDescription("Schedules a message for delivery in the future.")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("delay")
                .WithDescription("The duration to wait expressed as a number suffixed with 'd', 'h', or 'm' for scale.")
                .WithType(ApplicationCommandOptionType.String)
                .WithRequired(true)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("message")
                .WithDescription("The message to send after the delay.")
                .WithType(ApplicationCommandOptionType.String)
                .WithRequired(true)
            )
            .Build();

        public override async Task HandleCommandAsync(SocketSlashCommand payload)
        {
            string errorMessage = default;
            string delayInput = payload.Data.Options.FirstOrDefault(x => x.Name == "delay")?.Value as string;
            if (string.IsNullOrEmpty(delayInput))
            {
                errorMessage = "`delay` must be supplied.";
            }
            (string msg, TimeSpan parsed) delay = GetTimeSpan(delayInput);
            if (delay.msg != default)
            {
                errorMessage = delay.msg;
            }
            else if (delay.parsed > TimeSpan.FromDays(30))
            {
                errorMessage = $"Delay cannot last more than 30 days. (Is currently `{delay.parsed}`)";
            }
            else if (delay.parsed < TimeSpan.FromSeconds(30))
            {
                errorMessage = $"Delay cannot last less than 30 days. (Is currently `{delay.parsed}`)";
            }

            string messageInput = payload.Data.Options.FirstOrDefault(x => x.Name == "message")?.Value as string;
            if (string.IsNullOrEmpty(messageInput))
            {
                errorMessage = "`message` must be supplied.";
            }

            if (!payload.ChannelId.HasValue)
            {
                errorMessage = "ChannelId did not have a value.";
            }

            if (errorMessage != default)
            {
                await payload.RespondAsync(errorMessage, ephemeral: true);
                return;
            }

            // Allow time to respond to the command
            await payload.DeferAsync();

            using (IServiceScope scope = this.serviceProvider.CreateScope())
            using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
            {
                _ = poContext.ScheduledMessages.Add(
                    new ScheduledMessage
                    {
                        ChannelId = payload.ChannelId.Value,
                        Author = payload.User.Username,
                        Message = messageInput,
                        ScheduledDate = DateTimeOffset.UtcNow.Add(delay.parsed)
                    });
                _ = await poContext.SaveChangesAsync();
            }

            this.delays.ScheduledMessage.CancelDelay();
            _ = await payload.FollowupAsync($"Mesage scheduled for {delay.parsed} from now.", ephemeral: true);
        }
    }
}
