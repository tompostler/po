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
    /// Whether a command needs enabling is read from the <c>SlashCommands</c> rows that Discord registration keeps, which stay until Discord is removed.
    /// Removing a command deletes its <see cref="SlashCommandChannel"/> row, which for <c>po</c> also forgets the channel's container association, as on Discord.
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
            Version = 1,
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

            if (context.Subcommand == "list")
            {
                List<SlashCommand> commandsRequiringEnablement = await poContext.SlashCommands
                    .Where(x => x.RequiresChannelEnablement)
                    .Include(x => x.EnabledChannels)
                    .ToListAsync(context.CancellationToken);
                StringBuilder response = new();
                _ = response.AppendLine("Enabled commands:");
                _ = response.AppendLine(string.Join(", ", commandsRequiringEnablement.Where(c => c.EnabledChannels.Any(x => x.Channel == context.Channel)).Select(x => x.Name)));
                _ = response.AppendLine();
                _ = response.AppendLine("Disabled commands:");
                _ = response.AppendLine(string.Join(", ", commandsRequiringEnablement.Where(c => !c.EnabledChannels.Any(x => x.Channel == context.Channel)).Select(x => x.Name)));
                await context.ReplyAsync(response.ToString());
                return;
            }

            string commandName = context.GetString("command-name");
            SlashCommand command = await poContext.SlashCommands
                .Include(x => x.EnabledChannels)
                .SingleOrDefaultAsync(x => x.RequiresChannelEnablement && x.Name == commandName, context.CancellationToken);
            bool enabled = command?.EnabledChannels.Any(x => x.Channel == context.Channel) == true;

            if (command == default)
            {
                await context.ReplyAsync($"Could not find `{commandName}` in data store.");
            }
            else if (context.Subcommand == "add" && !enabled)
            {
                command.EnabledChannels.Add(new SlashCommandChannel { SlashCommandName = command.Name, Channel = context.Channel });
                _ = await poContext.SaveChangesAsync(context.CancellationToken);
                await context.ReplyAsync($"Enabled `{commandName}` for the current channel.");
            }
            else if (context.Subcommand == "remove" && enabled)
            {
                command.EnabledChannels = command.EnabledChannels.Where(x => x.Channel != context.Channel).ToList();
                _ = await poContext.SaveChangesAsync(context.CancellationToken);
                await context.ReplyAsync($"Disabled `{commandName}` for the current channel.");
            }
            else
            {
                await context.ReplyAsync("There was apparently nothing to do.");
            }
        }
    }
}
