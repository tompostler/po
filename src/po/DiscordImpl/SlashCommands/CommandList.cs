using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using po.DataAccess;
using po.Models;
using System.Text;

namespace po.DiscordImpl.SlashCommands
{
    public sealed class CommandList : SlashCommandBase
    {
        private readonly IServiceProvider serviceProvider;

        public CommandList(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
        }

        // Since the command has to be updated every time a new one is added to the list, just maintain it manually here
        // Don't forget to increase the version below to force a re-registration
        private readonly string[] commandNames = new[]
        {
            "nuke-messages",
            "nuke-messages-older-than-day",
            "po",
        };

        public override SlashCommand ExpectedCommand => new()
        {
            Name = "commandlist",
            Version = this.commandNames.Length,
            IsGuildLevel = true
        };

        public override SlashCommandProperties BuiltCommand => new SlashCommandBuilder()
            .WithName(this.ExpectedCommand.Name)
            .WithDescription("Configures which commands are enabled or disabled for a channel.")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("add")
                .WithDescription("Enable (add) a command to the channel enabled list.")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("command-name")
                    .WithDescription("The name of the command to enable for this channel.")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(true)
                    .AddCommandChoices(this.commandNames)
                )
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("remove")
                .WithDescription("Disable (remove) a command from the channel enabled list.")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("command-name")
                    .WithDescription("The name of the command to disable for this channel.")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(true)
                    .AddCommandChoices(this.commandNames)
                )
            )
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("list")
                .WithDescription("Lists the current status of the commands from the channel enabled list.")
                .WithType(ApplicationCommandOptionType.SubCommand)
            )
            .Build();

        public override async Task HandleCommandAsync(SocketSlashCommand payload)
        {
            string operation = payload.Data.Options.First().Name;

            if (operation == "list")
            {
                List<SlashCommand> commandsRequiringEnablement;
                using (IServiceScope scope = this.serviceProvider.CreateScope())
                using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
                {
                    commandsRequiringEnablement = await poContext.SlashCommands.Where(x => x.RequiresChannelEnablement).Include(x => x.EnabledChannels).ToListAsync();
                }
                StringBuilder response = new();
                _ = response.AppendLine();
                _ = response.AppendLine("Enabled commands:");
                _ = response.AppendLine(string.Join(", ", commandsRequiringEnablement.Where(c => c.EnabledChannels.Any(x => x.ChannelId == payload.Channel.Id)).Select(x => x.Name)));
                _ = response.AppendLine();
                _ = response.AppendLine("Disabled commands:");
                _ = response.AppendLine(string.Join(", ", commandsRequiringEnablement.Where(c => !c.EnabledChannels.Any(x => x.ChannelId == payload.Channel.Id)).Select(x => x.Name)));
                await payload.RespondAsync(response.ToString());
                return;
            }

            string commandName = payload.Data.Options.First().Options.First().Value as string;
            using (IServiceScope scope = this.serviceProvider.CreateScope())
            using (PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>())
            {
                SlashCommand commandRequiringEnablement = await poContext.SlashCommands.Include(x => x.EnabledChannels).SingleOrDefaultAsync(x => x.RequiresChannelEnablement && x.Name == commandName.ToLower());
                if (commandRequiringEnablement == default)
                {
                    await payload.RespondAsync($"Could not find `{commandName}` in data store.");
                }
                else if (operation == "add" && !commandRequiringEnablement.EnabledChannels.Any(x => x.ChannelId == payload.Channel.Id))
                {
                    commandRequiringEnablement.EnabledChannels.Add(new SlashCommandChannel { SlashCommandName = commandRequiringEnablement.Name, ChannelId = payload.Channel.Id });
                    _ = await poContext.SaveChangesAsync();
                    await payload.RespondAsync($"Enabled `{commandName}` for the current channel.");
                }
                else if (operation == "remove" && commandRequiringEnablement.EnabledChannels.Any(x => x.ChannelId == payload.Channel.Id))
                {
                    commandRequiringEnablement.EnabledChannels = commandRequiringEnablement.EnabledChannels.Where(x => x.ChannelId != payload.Channel.Id).ToList();
                    _ = await poContext.SaveChangesAsync();
                    await payload.RespondAsync($"Disabled `{commandName}` for the current channel.");
                }
                else
                {
                    await payload.RespondAsync("There was apparently nothing to do.");
                }
            }
        }
    }

    public static class CommandsRequiringEnablement
    {
        public static SlashCommandOptionBuilder AddCommandChoices(this SlashCommandOptionBuilder @this, string[] commandNames)
        {
            foreach (string name in commandNames)
            {
                _ = @this.AddChoice(name, name);
            }

            return @this;
        }
    }
}
