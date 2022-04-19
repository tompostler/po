using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using po.DataAccess;
using po.Models;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace po.DiscordImpl.SlashCommands
{
    public class PoCommand : SlashCommandBase
    {
        private readonly IServiceProvider serviceProvider;
        private readonly PoStorage poStorage;

        public PoCommand(
            IServiceProvider serviceProvider,
            PoStorage poStorage)
        {
            this.serviceProvider = serviceProvider;
            this.poStorage = poStorage;
        }

        public override SlashCommand ExpectedCommand => new()
        {
            Name = "po",
            Version = 2,
            IsGuildLevel = true,
            RequiresChannelEnablement = true
        };

        public override SlashCommandProperties BuiltCommand => new SlashCommandBuilder()
            .WithName(this.ExpectedCommand.Name)
            .WithDescription("Displays images from blob storage.")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("associate")
                .WithDescription("Associates a blob container of images with this channel.")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("container-name")
                    .WithDescription("The name of the container to associate, max one per channel.")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(true)
                )
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("disassociate")
                .WithDescription("Disassociates the currently associated blob container from this channel.")
                .WithType(ApplicationCommandOptionType.SubCommand)
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("reset")
                .WithDescription("Reset the current view statistics for a category.")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("category")
                    .WithDescription("Use 'all' for all categories.")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(true)
                )
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("show")
                .WithDescription("Displays a single image.")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("category")
                    .WithDescription("The category (or category prefix) of image to display.")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(false)
                )
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("status")
                .WithDescription("Generates a status report of viewed images.")
                .WithType(ApplicationCommandOptionType.SubCommand)
            )
            .Build();

        public override async Task HandleCommandAsync(SocketSlashCommand payload)
        {
            string operation = payload.Data.Options.First().Name;
            using IServiceScope scope = this.serviceProvider.CreateScope();
            using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();
            SlashCommandChannel command = await poContext.SlashCommandChannels.SingleAsync(sc => sc.SlashCommandName == payload.CommandName && sc.ChannelId == payload.ChannelId);

            if (operation == "associate")
            {
                string containerName = payload.Data.Options.First().Options.First().Value as string;
                if (!await this.poStorage.ContainerExistsAsync(containerName))
                {
                    await payload.RespondAsync($"Container `{containerName}` does not exist and cannot be associated with the current channel.");
                }
                else if (!string.IsNullOrWhiteSpace(command.RegistrationData))
                {
                    await payload.RespondAsync($"This channel is already associated with container `{containerName}`. To disassociate, use `/po disassociate`.");
                }
                else
                {
                    command.RegistrationData = containerName;
                    _ = await poContext.SaveChangesAsync();
                    await payload.RespondAsync($"Container `{containerName}` associated with this channel successfully.");
                }
                return;
            }
            if (operation == "disassociate")
            {
                if (string.IsNullOrWhiteSpace(command.RegistrationData))
                {
                    await payload.RespondAsync("This channel is already not associated with any containers.");
                }
                else
                {
                    string oldContainerName = command.RegistrationData;
                    command.RegistrationData = null;
                    _ = await poContext.SaveChangesAsync();
                    await payload.RespondAsync($"Channel successfully disassociated with container `{oldContainerName}`.");
                }
                return;
            }
            if (string.IsNullOrWhiteSpace(command.RegistrationData))
            {
                await payload.RespondAsync("This channel is not associated with any containers, and needs to be to be usable. Try `/po associate <container-name>`.");
                return;
            }

            if (operation == "status")
            {
                var statuses = await poContext.Blobs
                                .Where(b => b.ContainerName == command.RegistrationData)
                                .GroupBy(b => b.Category)
                                .Select(g => new
                                {
                                    g.Key,
                                    CountSeen = g.Count(b => b.Seen),
                                    CountUnseen = g.Count(b => !b.Seen)
                                })
                                .ToListAsync();

                StringBuilder response = new();
                int catLen = Math.Max("category".Length, statuses.Max(x => x.Key.Length));
                int numLen = "percent viewed".Length;
                _ = response.AppendLine("```");
                _ = response.AppendLine($"{"CATEGORY".PadRight(catLen)}  {"SEEN".PadLeft(numLen)}  {"UNSEEN".PadLeft(numLen)}  {"TOTAL".PadLeft(numLen)}  {"SEEN".PadLeft(numLen)}  {"PERCENT VIEWED".PadLeft(numLen)}");
                foreach (var status in statuses)
                {
                    _ = response.Append(status.Key.PadRight(catLen));
                    _ = response.Append("  ");
                    _ = response.Append(status.CountSeen.ToString().PadLeft(numLen));
                    _ = response.Append("  ");
                    _ = response.Append(status.CountUnseen.ToString().PadLeft(numLen));
                    _ = response.Append("  ");
                    int loctot = status.CountSeen + status.CountUnseen;
                    _ = response.Append(loctot.ToString().PadLeft(numLen));
                    _ = response.Append("  ");
                    _ = response.Append((1d * status.CountSeen / loctot).ToString("P2").PadLeft(numLen));
                    _ = response.AppendLine();
                }
                _ = response.Append("TOTAL".PadRight(catLen));
                _ = response.Append("  ");
                _ = response.Append(statuses.Sum(x => x.CountSeen).ToString().PadLeft(numLen));
                _ = response.Append("  ");
                _ = response.Append(statuses.Sum(x => x.CountUnseen).ToString().PadLeft(numLen));
                _ = response.Append("  ");
                int ovetot = statuses.Sum(x => x.CountSeen + x.CountUnseen);
                _ = response.Append(ovetot.ToString().PadLeft(numLen));
                _ = response.Append("  ");
                _ = response.Append((1d * statuses.Sum(x => x.CountSeen) / ovetot).ToString("P2").PadLeft(numLen));
                _ = response.AppendLine();
                await payload.RespondAsync(response.ToString());
                return;
            }

            string category = payload.Data.Options.First().Options.First().Value as string;

            throw new NotImplementedException($"`{operation}` with category `{category ?? "<null>"}` is not complete.");
        }
    }
}
