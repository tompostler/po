using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using po.DataAccess;
using po.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace po.DiscordImpl.SlashCommands
{
    public class PoConfigure : SlashCommandBase
    {
        private readonly IServiceProvider serviceProvider;
        private readonly PoStorage poStorage;
        private readonly ILogger<PoConfigure> logger;

        public PoConfigure(
            IServiceProvider serviceProvider,
            PoStorage poStorage,
            ILogger<PoConfigure> logger)
        {
            this.serviceProvider = serviceProvider;
            this.poStorage = poStorage;
            this.logger = logger;
        }

        public override SlashCommand ExpectedCommand => new()
        {
            Name = "po-configure",
            Version = 2,
            IsGuildLevel = true
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
                .WithName("rescan")
                .WithDescription("For a currently associated container, rescan its contents. Only adds blobs (the daily scan will eventually remove blobs).")
                .WithType(ApplicationCommandOptionType.SubCommand)
            )
            .Build();

        public override async Task HandleCommandAsync(SocketSlashCommand payload)
        {
            string operation = payload.Data.Options.First().Name;
            using IServiceScope scope = this.serviceProvider.CreateScope();
            using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();
            SlashCommandChannel command = await poContext.SlashCommandChannels.SingleOrDefaultAsync(sc => sc.SlashCommandName == "po" && sc.ChannelId == payload.ChannelId);

            if (operation == "associate")
            {
                string containerName = payload.Data.Options.First().Options.First().Value as string;
                if (!await this.poStorage.ContainerExistsAsync(containerName))
                {
                    await payload.RespondAsync($"Container `{containerName}` does not exist and cannot be associated with the current channel.");
                }
                else if (!string.IsNullOrWhiteSpace(command?.RegistrationData))
                {
                    await payload.RespondAsync($"This channel is already associated with container `{containerName}`. To disassociate, use `/po-configure disassociate`.");
                }
                else
                {
                    // New registration
                    if (command == default)
                    {
                        command = new()
                        {
                            SlashCommandName = "po",
                            ChannelId = payload.ChannelId.Value,
                        };
                        _ = poContext.SlashCommandChannels.Add(command);
                    }
                    command.RegistrationData = containerName;
                    _ = await poContext.SaveChangesAsync();
                    await payload.RespondAsync($"Container `{containerName}` associated with this channel successfully.");
                }
            }
            else if (string.IsNullOrWhiteSpace(command?.RegistrationData))
            {
                await payload.RespondAsync($"This channel is not associated with any containers, and needs to be for any further commands to perform an action (such as {operation}). Try `/po-configure associate <container-name>`.");
            }
            else if (operation == "disassociate")
            {
                string oldContainerName = command.RegistrationData;
                command.RegistrationData = null;
                _ = await poContext.SaveChangesAsync();
                await payload.RespondAsync($"Channel successfully disassociated with container `{oldContainerName}`.");
            }
            else if (operation == "rescan")
            {
                await payload.DeferAsync();
                Dictionary<string, Services.Background.SyncBlobMetadataBackgroundService.CountValue> counts = new();
                await Services.Background.SyncBlobMetadataBackgroundService.FindAndAddNewBlobsAsync(this.serviceProvider, this.poStorage, this.logger, counts, CancellationToken.None, containerName: command.RegistrationData);
                string response = Services.Background.SyncBlobMetadataBackgroundService.BuildReportFromCounts(counts, this.logger);

                if (response?.Length > 2000)
                {
                    var fileStream = new MemoryStream(Encoding.UTF8.GetBytes(response.Trim().Trim('`')));
                    _ = await payload.FollowupWithFileAsync(fileStream, "update.txt", "The rescan was too long for a message:");
                }
                else
                {
                    _ = await payload.FollowupAsync(response ?? "No updates.");
                }
            }
            else
            {
                // Default behavior is to throw up
                throw new NotImplementedException($"`{operation}` is not complete.");
            }
        }
    }
}
