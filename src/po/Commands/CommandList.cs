using Microsoft.EntityFrameworkCore;
using po.DataAccess;
using po.Models;
using System.Text;

namespace po.Commands
{
    /// <summary>
    /// Enables, disables and lists the commands that only run in channels where they've been enabled.
    /// </summary>
    /// <remarks>
    /// The names it offers are <see cref="CommandNames"/>, which has to match the <see cref="CommandDefinition.EnablementGroup"/> of every definition with <see cref="CommandDefinition.RequiresChannelEnablement"/>.
    /// Removing a command deletes its <see cref="CommandChannel"/> row, which for <c>po</c> also forgets the channel's container association.
    /// </remarks>
    public sealed class CommandList : ICommand
    {
        // Maintained by hand, because the dispatcher's definitions can't be injected here without a dependency cycle.
        private static readonly string[] CommandNames =
        [
            "nuke-bot-messages",
            "nuke-messages",
            "po",
        ];

        private readonly IServiceProvider serviceProvider;

        public CommandList(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
        }

        public CommandDefinition Definition { get; } = new()
        {
            Name = "commandlist",
            Description = "Configures which commands are enabled or disabled for a channel.",
            Subcommands =
            [
                new SubcommandDefinition
                {
                    Name = "add",
                    Description = "Enable (add) a command to the channel enabled list.",
                    Options = [new CommandOption { Name = "command-name", Description = "The name of the command to enable for this channel.", Type = CommandOptionType.String, Required = true, Choices = CommandNames }],
                },
                new SubcommandDefinition
                {
                    Name = "remove",
                    Description = "Disable (remove) a command from the channel enabled list.",
                    Options = [new CommandOption { Name = "command-name", Description = "The name of the command to disable for this channel.", Type = CommandOptionType.String, Required = true, Choices = CommandNames }],
                },
                new SubcommandDefinition
                {
                    Name = "list",
                    Description = "Lists the current status of the commands from the channel enabled list.",
                },
            ],
        };

        public async Task HandleAsync(CommandContext context)
        {
            using IServiceScope scope = this.serviceProvider.CreateScope();
            using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();
            HashSet<string> enabled = (await poContext.CommandChannels
                .Where(x => x.Platform == context.Channel.Platform && x.ChannelId == context.Channel.Id)
                .Select(x => x.CommandName)
                .ToListAsync(context.CancellationToken))
                .ToHashSet();

            if (context.Subcommand == "list")
            {
                StringBuilder response = new();
                _ = response.AppendLine("Enabled commands:");
                _ = response.AppendLine(string.Join(", ", CommandNames.Where(enabled.Contains)));
                _ = response.AppendLine();
                _ = response.AppendLine("Disabled commands:");
                _ = response.AppendLine(string.Join(", ", CommandNames.Where(x => !enabled.Contains(x))));
                await context.ReplyAsync(response.ToString());
                return;
            }

            // The parser has already checked the name against CommandNames, the option's choices.
            string commandName = context.GetString("command-name");
            if (context.Subcommand == "add" && !enabled.Contains(commandName))
            {
                _ = poContext.CommandChannels.Add(new CommandChannel { CommandName = commandName, Channel = context.Channel });
                _ = await poContext.SaveChangesAsync(context.CancellationToken);
                await context.ReplyAsync($"Enabled `{commandName}` for the current channel.");
            }
            else if (context.Subcommand == "remove" && enabled.Contains(commandName))
            {
                _ = await poContext.CommandChannels
                    .Where(x => x.CommandName == commandName && x.Platform == context.Channel.Platform && x.ChannelId == context.Channel.Id)
                    .ExecuteDeleteAsync(context.CancellationToken);
                await context.ReplyAsync($"Disabled `{commandName}` for the current channel.");
            }
            else
            {
                await context.ReplyAsync("There was apparently nothing to do.");
            }
        }
    }
}
