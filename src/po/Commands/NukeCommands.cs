using po.Chat;

namespace po.Commands
{
    /// <summary>
    /// Deletes a channel's messages older than a number of days, looking back over at most <see cref="MessageLimit"/> recent messages.
    /// </summary>
    /// <remarks>
    /// The purge runs after the command endpoint has answered, so it can take as long as it needs; if a restart stops it partway, running it again picks up the rest.
    /// </remarks>
    public abstract class NukeCommand : ICommand
    {
        private const int MessageLimit = 1_000;

        private readonly ChatRouter chatRouter;

        protected NukeCommand(ChatRouter chatRouter)
        {
            this.chatRouter = chatRouter;
        }

        public abstract CommandDefinition Definition { get; }

        /// <summary>
        /// Whether only messages from bots and integrations are deleted.
        /// </summary>
        protected abstract bool BotsOnly { get; }

        protected static IReadOnlyList<CommandOption> DaysOption { get; } =
        [
            new CommandOption { Name = "days", Description = "The number of days ago to delete messages older than", Type = CommandOptionType.Number },
        ];

        public async Task HandleAsync(CommandContext context)
        {
            double daysAgo = context.GetNumber("days") ?? 1;
            string which = this.BotsOnly ? "bot messages" : "messages";
            await context.ReplyAsync($"Starting purge of all {which} in channel older than {daysAgo} days.");

            DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-daysAgo);
            int count = 0;
            foreach (ChannelMessage message in await this.chatRouter.ListMessagesAsync(context.Channel, MessageLimit, context.CancellationToken))
            {
                if (message.PostedAt < cutoff && (!this.BotsOnly || message.FromBot))
                {
                    await this.chatRouter.DeleteMessageAsync(context.Channel, message.Id, context.CancellationToken);
                    count++;
                }
            }

            await context.ReplyAsync(this.BotsOnly ? $"Removed {count} old bot messages in channel." : $"Purged {count} messages in channel.");
        }
    }

    public sealed class NukeMessages : NukeCommand
    {
        public NukeMessages(ChatRouter chatRouter)
            : base(chatRouter)
        {
        }

        public override CommandDefinition Definition { get; } = new()
        {
            Name = "nuke-messages",
            Description = "Removes all messages from the channel older than a specified amount (if requested).",
            RequiresChannelEnablement = true,
            Options = DaysOption,
        };

        protected override bool BotsOnly => false;
    }

    public sealed class NukeBotMessages : NukeCommand
    {
        public NukeBotMessages(ChatRouter chatRouter)
            : base(chatRouter)
        {
        }

        public override CommandDefinition Definition { get; } = new()
        {
            Name = "nuke-bot-messages",
            Description = "Removes all bot messages from the channel older than a specified amount (if requested).",
            RequiresChannelEnablement = true,
            Options = DaysOption,
        };

        protected override bool BotsOnly => true;
    }
}
