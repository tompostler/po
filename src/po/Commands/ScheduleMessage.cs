using po.DataAccess;
using po.Utilities;

namespace po.Commands
{
    /// <summary>
    /// Schedules a message that <see cref="Services.Background.ScheduledMessageBackgroundService"/> posts in the channel later.
    /// </summary>
    public sealed class ScheduleMessage : ICommand
    {
        private static readonly TimeSpan MinimumDelay = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan MaximumDelay = TimeSpan.FromDays(30);

        private readonly IServiceProvider serviceProvider;
        private readonly Delays delays;

        public ScheduleMessage(
            IServiceProvider serviceProvider,
            Delays delays)
        {
            this.serviceProvider = serviceProvider;
            this.delays = delays;
        }

        public CommandDefinition Definition { get; } = new()
        {
            Name = "schedule-message",
            Description = "Schedules a message for delivery in the future.",
            Version = 1,
            Options =
            [
                new CommandOption { Name = "delay", Description = "The duration to wait expressed as a number suffixed with 'd', 'h', 'm', or 's' for scale.", Type = CommandOptionType.String, Required = true },
                new CommandOption { Name = "message", Description = "The message to send after the delay.", Type = CommandOptionType.String, Required = true },
            ],
        };

        public async Task HandleAsync(CommandContext context)
        {
            (string error, TimeSpan delay) = DurationParser.Parse(context.GetString("delay"));
            if (error == default && delay > MaximumDelay)
            {
                error = $"Delay cannot last more than 30 days. (Is currently `{delay}`)";
            }
            else if (error == default && delay < MinimumDelay)
            {
                error = $"Delay cannot last less than 30 seconds. (Is currently `{delay}`)";
            }

            if (error != default)
            {
                await context.ReplyAsync(error, ephemeral: true);
                return;
            }

            using (IServiceScope scope = this.serviceProvider.CreateScope())
            using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
            {
                _ = poContext.ScheduledMessages.Add(
                    new Models.ScheduledMessage
                    {
                        Channel = context.Channel,
                        Author = context.UserName,
                        Message = context.GetString("message"),
                        ScheduledDate = DateTimeOffset.UtcNow.Add(delay),
                    });
                _ = await poContext.SaveChangesAsync(context.CancellationToken);
            }

            this.delays.ScheduledMessage.CancelDelay();
            await context.ReplyAsync($"Message scheduled for {delay} from now.", ephemeral: true);
        }
    }
}
