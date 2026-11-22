# Commands

po's slash commands are written once against `Commands/`, and each platform supplies the registration, the request handling and a `CommandContext`.
Only Mattermost runs these commands.
The Discord commands in `DiscordImpl/` are older copies that keep working until Discord is removed, rather than ports, because the domain logic they call is already shared from `Services/Po/`.

## Flow on Mattermost

1. At startup, `MattermostCommandRegistrar` registers one Mattermost slash command per `CommandDefinition`, pointing at po's internal port.
2. When someone runs one, `MattermostController` checks its token, finds the command with `CommandDispatcher.TryGetCommand`, and parses the text with `TextArgumentParser`.
   A bad token, an unknown command or a parse error is answered right there.
3. Otherwise the controller answers Mattermost with an empty 200 and runs the command in the background through `CommandDispatcher.DispatchAsync`.
4. The dispatcher checks channel enablement, runs the command, and turns any exception into a reply.

## Writing a command

- Implement `ICommand`, and register it in `Program.cs` as `services.AddSingleton<ICommand, YourCommand>()`; the dispatcher and the registrar pick it up from there.
- Mattermost sends a command's arguments as one string, so the definition is also the parser's grammar: the subcommand comes first, then each option in order, and a string option in the last position takes the rest of the text.
  Put optional options last, because there are no named arguments.
- A command name can't be one Mattermost already uses, such as `/echo`, because built-in and plugin commands run first; the registrar skips such a command with a warning.
- Reply through the context: `ReplyAsync(text)` posts in the channel and falls back to a file for long text, and `ephemeral: true` shows a reply only to the person who ran the command.
  Ephemeral replies use Mattermost's `response_url`, which allows 5 replies per command, so keep them to one or two.
- `RequiresChannelEnablement` commands only run in channels where `/commandlist add` has enabled them, and `/commandlist` offers them by a name list that's kept by hand in `CommandList.cs`.
  That check reads the `SlashCommands` rows from Discord registration, so a new enablement-gated command also needs one until Discord is removed.
