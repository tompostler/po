using Microsoft.EntityFrameworkCore;
using po.DataAccess;
using po.Models;
using po.Services.Po;

namespace po.Commands
{
    /// <summary>
    /// Associates a channel with the image container that <c>/po</c> shows from, and rescans that container.
    /// </summary>
    /// <remarks>
    /// The association is the <c>RegistrationData</c> on the channel's <c>po</c> enablement row, so associating also enables <c>/po</c> in the channel, and <c>/commandlist remove po</c> forgets the association.
    /// </remarks>
    public sealed class PoConfigure : ICommand
    {
        private const string PoCommandName = "po";

        private readonly IServiceProvider serviceProvider;
        private readonly IPoStorage poStorage;
        private readonly PoBlobSync poBlobSync;

        public PoConfigure(
            IServiceProvider serviceProvider,
            IPoStorage poStorage,
            PoBlobSync poBlobSync)
        {
            this.serviceProvider = serviceProvider;
            this.poStorage = poStorage;
            this.poBlobSync = poBlobSync;
        }

        public CommandDefinition Definition { get; } = new()
        {
            Name = "po-configure",
            Description = "Associates this channel with a container of images for /po.",
            Subcommands =
            [
                new SubcommandDefinition
                {
                    Name = "associate",
                    Description = "Associates a blob container of images with this channel.",
                    Options = [new CommandOption { Name = "container-name", Description = "The name of the container to associate, max one per channel.", Type = CommandOptionType.String, Required = true }],
                },
                new SubcommandDefinition
                {
                    Name = "disassociate",
                    Description = "Disassociates the currently associated blob container from this channel.",
                },
                new SubcommandDefinition
                {
                    Name = "rescan",
                    Description = "For a currently associated container, rescan its contents. Only adds blobs.",
                },
            ],
        };

        public async Task HandleAsync(CommandContext context)
        {
            using IServiceScope scope = this.serviceProvider.CreateScope();
            using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();
            CommandChannel association = await poContext.CommandChannels.SingleOrDefaultAsync(
                x => x.CommandName == PoCommandName && x.Platform == context.Channel.Platform && x.ChannelId == context.Channel.Id,
                context.CancellationToken);

            if (context.Subcommand == "associate")
            {
                string containerName = context.GetString("container-name");
                if (!await this.poStorage.ContainerExistsAsync(containerName))
                {
                    await context.ReplyAsync($"Container `{containerName}` does not exist and cannot be associated with the current channel.");
                }
                else if (!string.IsNullOrWhiteSpace(association?.RegistrationData))
                {
                    await context.ReplyAsync($"This channel is already associated with container `{association.RegistrationData}`. To disassociate, use `/po-configure disassociate`.");
                }
                else
                {
                    if (association == default)
                    {
                        association = new CommandChannel { CommandName = PoCommandName, Channel = context.Channel };
                        _ = poContext.CommandChannels.Add(association);
                    }
                    association.RegistrationData = containerName;
                    _ = await poContext.SaveChangesAsync(context.CancellationToken);
                    await context.ReplyAsync($"Container `{containerName}` associated with this channel successfully.");
                }
            }
            else if (string.IsNullOrWhiteSpace(association?.RegistrationData))
            {
                await context.ReplyAsync($"This channel is not associated with any containers, and needs to be for any further commands to perform an action (such as {context.Subcommand}). Try `/po-configure associate <container-name>`.");
            }
            else if (context.Subcommand == "disassociate")
            {
                string oldContainerName = association.RegistrationData;
                association.RegistrationData = null;
                _ = await poContext.SaveChangesAsync(context.CancellationToken);
                await context.ReplyAsync($"Channel successfully disassociated with container `{oldContainerName}`.");
            }
            else if (context.Subcommand == "rescan")
            {
                Dictionary<string, PoBlobSync.CountValue> counts = [];
                await this.poBlobSync.FindAndAddNewBlobsAsync(counts, context.CancellationToken, containerName: association.RegistrationData);
                await context.ReplyAsync(
                    this.poBlobSync.BuildReportFromCounts(counts) ?? "No updates.",
                    fileName: "update.txt",
                    fileText: "The rescan was too long for a message:");
            }
        }
    }
}
