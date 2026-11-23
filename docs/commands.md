# Commands

po's slash commands are written once against `Commands/`, and the platform supplies the registration, the request handling and a `CommandContext`.
Mattermost is the only platform now, but the commands don't depend on it, so another platform would only need its own registrar, endpoint and context.

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
- `RequiresChannelEnablement` commands only run in channels where `/commandlist add` has enabled their `EnablementGroup`, which defaults to the command's name; commands that share a group are enabled together.
  `/commandlist` offers the groups from a list kept by hand in `CommandList.cs`, because building it from the definitions would be a dependency cycle, and `CommandListTests` fails if the two drift apart.
- The `po` group's enablement row also holds the channel's image container, so any command in that group can read it, and `/commandlist remove po` forgets it.
