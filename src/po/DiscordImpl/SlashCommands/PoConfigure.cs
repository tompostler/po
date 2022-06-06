using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using po.DataAccess;
using po.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace po.DiscordImpl.SlashCommands
{
    public class PoConfigure : SlashCommandBase
    {
        private readonly IServiceProvider serviceProvider;
        private readonly PoStorage poStorage;

        public PoConfigure(
            IServiceProvider serviceProvider,
            PoStorage poStorage)
        {
            this.serviceProvider = serviceProvider;
            this.poStorage = poStorage;
        }

        public override SlashCommand ExpectedCommand => new()
        {
            Name = "po-configure",
            Version = 1,
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
                    command = new()
                    {
                        SlashCommandName = "po",
                        ChannelId = payload.ChannelId.Value,
                        RegistrationData = containerName
                    };
                    _ = poContext.SlashCommandChannels.Add(command);
                    _ = await poContext.SaveChangesAsync();
                    await payload.RespondAsync($"Container `{containerName}` associated with this channel successfully.");
                }
            }
            else if (operation == "disassociate")
            {
                if (string.IsNullOrWhiteSpace(command?.RegistrationData))
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
            }
            else if (string.IsNullOrWhiteSpace(command?.RegistrationData))
            {
                await payload.RespondAsync("This channel is not associated with any containers, and needs to be to be usable. Try `/po-configure associate <container-name>`.");
            }
            else
            {
                // Default behavior is to throw up
                throw new NotImplementedException($"`{operation}` is not complete.");
            }
        }
    }
}
