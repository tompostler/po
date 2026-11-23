using Microsoft.EntityFrameworkCore;
using po.DataAccess;
using po.Services.Po;

namespace po.Commands
{
    /// <summary>
    /// The image commands, which work on the container that <c>/po-configure associate</c> gave the channel.
    /// </summary>
    /// <remarks>
    /// They're separate commands rather than subcommands of one <c>/po</c>, so Mattermost's autocomplete shows each one's own description and argument hint.
    /// They share the <c>po</c> enablement group, so <c>/commandlist add po</c> enables them all, and the group's row is where the container is stored.
    /// </remarks>
    public abstract class PoCommandBase : ICommand
    {
        public const string Group = "po";

        protected const string DurationDescription = "expressed as a number suffixed with 'd', 'h', 'm', or 's' for scale.";

        protected static readonly CommandOption OptionalCategory = new() { Name = "category", Description = "The category (or category prefix) of images.", Type = CommandOptionType.String };

        private readonly IServiceProvider serviceProvider;

        protected PoCommandBase(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
        }

        public abstract CommandDefinition Definition { get; }

        public async Task HandleAsync(CommandContext context)
        {
            string containerName;
            using (IServiceScope scope = this.serviceProvider.CreateScope())
            using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
            {
                containerName = await poContext.CommandChannels
                    .Where(x => x.CommandName == this.Definition.EnablementGroup && x.Platform == context.Channel.Platform && x.ChannelId == context.Channel.Id)
                    .Select(x => x.RegistrationData)
                    .SingleOrDefaultAsync(context.CancellationToken);
            }

            if (string.IsNullOrWhiteSpace(containerName))
            {
                await context.ReplyAsync("This channel is not associated with any containers, and needs to be to be usable. Try `/po-configure associate <container-name>`.");
                return;
            }

            await this.HandleAsync(context, containerName);
        }

        protected abstract Task HandleAsync(CommandContext context, string containerName);

        protected static CommandDefinition Define(string name, string description, params CommandOption[] options) => new()
        {
            Name = name,
            Description = description,
            RequiresChannelEnablement = true,
            EnablementGroup = Group,
            Options = options,
        };
    }

    public sealed class PoShow : PoCommandBase
    {
        private readonly PoImageService poImageService;

        public PoShow(IServiceProvider serviceProvider, PoImageService poImageService)
            : base(serviceProvider)
        {
            this.poImageService = poImageService;
        }

        public override CommandDefinition Definition { get; } = Define("po-show", "Displays a single unseen image.", OptionalCategory);

        protected override async Task HandleAsync(CommandContext context, string containerName)
        {
            string category = context.GetString("category");
            PoImage image = await this.poImageService.PickUnseenAsync(containerName, category, context.UserName);
            if (image == default)
            {
                await context.ReplyAsync(PoImageService.NoneRemainingMessage(category));
                return;
            }

            await context.ReplyAsync(image.ToChatMessage());
            await this.poImageService.MarkSeenAsync(image);
        }
    }

    public sealed class PoRandom : PoCommandBase
    {
        private readonly PoScheduler poScheduler;

        public PoRandom(IServiceProvider serviceProvider, PoScheduler poScheduler)
            : base(serviceProvider)
        {
            this.poScheduler = poScheduler;
        }

        public override CommandDefinition Definition { get; } = Define(
            "po-random",
            "Schedules a number of images at random times over a duration.",
            new CommandOption { Name = "count", Description = "The number of images to display.", Type = CommandOptionType.Integer, Required = true },
            new CommandOption { Name = "duration", Description = $"The duration to display images {DurationDescription}", Type = CommandOptionType.String, Required = true },
            OptionalCategory);

        protected override async Task HandleAsync(CommandContext context, string containerName)
            => await context.ReplyAsync(await this.poScheduler.ScheduleRandomAsync(
                containerName,
                context.Channel,
                context.GetString("category"),
                context.UserName,
                context.GetInteger("count"),
                context.GetString("duration")));
    }

    public sealed class PoTimer : PoCommandBase
    {
        private readonly PoScheduler poScheduler;

        public PoTimer(IServiceProvider serviceProvider, PoScheduler poScheduler)
            : base(serviceProvider)
        {
            this.poScheduler = poScheduler;
        }

        public override CommandDefinition Definition { get; } = Define(
            "po-timer",
            "Schedules images at a fixed interval over a duration.",
            new CommandOption { Name = "interval", Description = $"The time between displayed images {DurationDescription}", Type = CommandOptionType.String, Required = true },
            new CommandOption { Name = "duration", Description = $"The duration to display images {DurationDescription}", Type = CommandOptionType.String, Required = true },
            OptionalCategory);

        protected override async Task HandleAsync(CommandContext context, string containerName)
            => await context.ReplyAsync(await this.poScheduler.ScheduleTimerAsync(
                containerName,
                context.Channel,
                context.GetString("category"),
                context.UserName,
                context.GetString("interval"),
                context.GetString("duration")));
    }

    public sealed class PoClear : PoCommandBase
    {
        private readonly PoScheduler poScheduler;

        public PoClear(IServiceProvider serviceProvider, PoScheduler poScheduler)
            : base(serviceProvider)
        {
            this.poScheduler = poScheduler;
        }

        public override CommandDefinition Definition { get; } = Define("po-clear", "Cancels the images scheduled for this channel.");

        protected override async Task HandleAsync(CommandContext context, string containerName)
            => await context.ReplyAsync(await this.poScheduler.ClearAsync(containerName, context.Channel));
    }

    public sealed class PoReset : PoCommandBase
    {
        private readonly PoImageService poImageService;

        public PoReset(IServiceProvider serviceProvider, PoImageService poImageService)
            : base(serviceProvider)
        {
            this.poImageService = poImageService;
        }

        public override CommandDefinition Definition { get; } = Define(
            "po-reset",
            "Marks a category's images as unseen again.",
            OptionalCategory with { Required = true });

        protected override async Task HandleAsync(CommandContext context, string containerName)
            => await context.ReplyAsync(await this.poImageService.ResetSeenAsync(containerName, context.GetString("category"), message => context.ReplyAsync(message)));
    }

    public sealed class PoStatus : PoCommandBase
    {
        private readonly PoStatusReport poStatusReport;

        public PoStatus(IServiceProvider serviceProvider, PoStatusReport poStatusReport)
            : base(serviceProvider)
        {
            this.poStatusReport = poStatusReport;
        }

        public override CommandDefinition Definition { get; } = Define("po-status", "Reports how many images in each category have been seen.");

        protected override async Task HandleAsync(CommandContext context, string containerName)
            => await context.ReplyAsync(
                await this.poStatusReport.BuildAsync(containerName),
                fileName: "po-status.txt",
                fileText: "The status report was too long for a message, so it has been posted as a file.");
    }
}
