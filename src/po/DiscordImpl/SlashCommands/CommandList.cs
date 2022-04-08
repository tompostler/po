using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using po.DataAccess;
using po.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace po.DiscordImpl.SlashCommands
{
    public sealed class CommandList : SlashCommandBase
    {
        private readonly IServiceProvider serviceProvider;

        // To support the CommandList, a parameterless constructor must be provided
        public CommandList() { }

        [ActivatorUtilitiesConstructor]
        public CommandList(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
        }

        public override SlashCommand ExpectedCommand => new()
        {
            Name = "commandlist",
            Version = 1,
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
                    .AddCommandChoices()
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
                    .AddCommandChoices()
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
        private static List<string> commandNames;

        public static SlashCommandOptionBuilder AddCommandChoices(this SlashCommandOptionBuilder @this)
        {
            if (commandNames == default)
            {
                // Gotta love reflection...
                // Normally we'd just use the DI container to get all the types, but unsurpringly it does not work well when you need an enumerable containing yourself at construction time
                Type baseType = typeof(SlashCommandBase);
                commandNames = Assembly.GetExecutingAssembly()
                    .GetTypes()
                    .Where(t => t != baseType && baseType.IsAssignableFrom(t))
                    .Select(t => (SlashCommandBase)Activator.CreateInstance(t))
                    .Where(t => t.ExpectedCommand.RequiresChannelEnablement)
                    .Select(x => x.ExpectedCommand.Name)
                    .ToList();
            }

            foreach (string name in commandNames)
            {
                _ = @this.AddChoice(name, name);
            }

            return @this;
        }
    }
}
