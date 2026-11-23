using Microsoft.EntityFrameworkCore;
using po.DataAccess;
using po.Services.Po;

namespace po.Commands
{
    /// <summary>
    /// Shows, schedules and reports on images from the container that <c>/po-configure associate</c> gave the channel.
    /// </summary>
    public sealed class PoCommand : ICommand
    {
        private const string DurationDescription = "expressed as a number suffixed with 'd', 'h', 'm', or 's' for scale.";

        private readonly IServiceProvider serviceProvider;
        private readonly PoImageService poImageService;
        private readonly PoScheduler poScheduler;
        private readonly PoStatusReport poStatusReport;

        public PoCommand(
            IServiceProvider serviceProvider,
            PoImageService poImageService,
            PoScheduler poScheduler,
            PoStatusReport poStatusReport)
        {
            this.serviceProvider = serviceProvider;
            this.poImageService = poImageService;
            this.poScheduler = poScheduler;
            this.poStatusReport = poStatusReport;
        }

        public CommandDefinition Definition { get; } = new()
        {
            Name = "po",
            Description = "Displays images from blob storage.",
            RequiresChannelEnablement = true,
            Subcommands =
            [
                new SubcommandDefinition
                {
                    Name = "clear",
                    Description = "Clear images from scheduled queue.",
                },
                new SubcommandDefinition
                {
                    Name = "random",
                    Description = "Schedule the display of a bunch of images.",
                    Options =
                    [
                        new CommandOption { Name = "count", Description = "The number of images to display.", Type = CommandOptionType.Integer, Required = true },
                        new CommandOption { Name = "duration", Description = $"The duration to display images {DurationDescription}", Type = CommandOptionType.String, Required = true },
                        new CommandOption { Name = "category", Description = "The category (or category prefix) for images to display.", Type = CommandOptionType.String },
                    ],
                },
                new SubcommandDefinition
                {
                    Name = "reset",
                    Description = "Reset the seen statistics.",
                    Options = [new CommandOption { Name = "category", Description = "The category (or category prefix) of images to reset the seen status.", Type = CommandOptionType.String, Required = true }],
                },
                new SubcommandDefinition
                {
                    Name = "show",
                    Description = "Displays a single image.",
                    Options = [new CommandOption { Name = "category", Description = "The category (or category prefix) of image to display.", Type = CommandOptionType.String }],
                },
                new SubcommandDefinition
                {
                    Name = "status",
                    Description = "Generates a status report of viewed images.",
                },
                new SubcommandDefinition
                {
                    Name = "timer",
                    Description = "Schedule the display of a bunch of images.",
                    Options =
                    [
                        new CommandOption { Name = "interval", Description = $"The time between displayed images {DurationDescription}", Type = CommandOptionType.String, Required = true },
                        new CommandOption { Name = "duration", Description = $"The duration to display images {DurationDescription}", Type = CommandOptionType.String, Required = true },
                        new CommandOption { Name = "category", Description = "The category (or category prefix) for images to display.", Type = CommandOptionType.String },
                    ],
                },
            ],
        };

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

            string category = context.GetString("category");
            switch (context.Subcommand)
            {
                case "clear":
                    await context.ReplyAsync(await this.poScheduler.ClearAsync(containerName, context.Channel));
                    break;

                case "random":
                    await context.ReplyAsync(await this.poScheduler.ScheduleRandomAsync(
                        containerName,
                        context.Channel,
                        category,
                        context.UserName,
                        context.GetInteger("count"),
                        context.GetString("duration")));
                    break;

                case "reset":
                    await context.ReplyAsync(await this.poImageService.ResetSeenAsync(containerName, category, message => context.ReplyAsync(message)));
                    break;

                case "show":
                    PoImage image = await this.poImageService.PickUnseenAsync(containerName, category, context.UserName);
                    if (image == default)
                    {
                        await context.ReplyAsync(PoImageService.NoneRemainingMessage(category));
                    }
                    else
                    {
                        await context.ReplyAsync(image.ToChatMessage());
                        await this.poImageService.MarkSeenAsync(image);
                    }
                    break;

                case "status":
                    await context.ReplyAsync(
                        await this.poStatusReport.BuildAsync(containerName),
                        fileName: "po-status.txt",
                        fileText: "The status report was too long for a message, so it has been posted as a file.");
                    break;

                case "timer":
                    await context.ReplyAsync(await this.poScheduler.ScheduleTimerAsync(
                        containerName,
                        context.Channel,
                        category,
                        context.UserName,
                        context.GetString("interval"),
                        context.GetString("duration")));
                    break;
            }
        }
    }
}
