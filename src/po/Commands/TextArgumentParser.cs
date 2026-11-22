using System.Globalization;
using System.Text.RegularExpressions;

namespace po.Commands
{
    /// <summary>
    /// Parses a command's arguments from the single text string that Mattermost sends, such as <c>random 5 2h cats</c> for <c>/po</c>.
    /// </summary>
    /// <remarks>
    /// The rules are kept small because people type these by hand.
    /// A command with subcommands takes the subcommand as its first word, matched without regard to case.
    /// Each following word fills the next option in definition order.
    /// A string option in the last position takes the rest of the text verbatim, so messages and echoes need no quoting.
    /// There are no named arguments, so an optional option can only be given when every option before it is.
    /// </remarks>
    public static partial class TextArgumentParser
    {
        public static bool TryParse(CommandDefinition definition, string text, out ParsedArguments parsed, out string error)
        {
            parsed = default;
            error = default;
            List<Match> words = Word().Matches(text ?? string.Empty).ToList();
            int next = 0;

            string subcommandName = default;
            IReadOnlyList<CommandOption> options = definition.Options;
            if (definition.Subcommands.Count > 0)
            {
                string subcommandList = string.Join(", ", definition.Subcommands.Select(x => $"`{x.Name}`"));
                if (words.Count == 0)
                {
                    error = $"`/{definition.Name}` needs a subcommand: {subcommandList}.";
                    return false;
                }

                SubcommandDefinition subcommand = definition.Subcommands.FirstOrDefault(x => string.Equals(x.Name, words[0].Value, StringComparison.OrdinalIgnoreCase));
                if (subcommand == default)
                {
                    error = $"`{words[0].Value}` is not a subcommand of `/{definition.Name}`. Try one of {subcommandList}.";
                    return false;
                }

                subcommandName = subcommand.Name;
                options = subcommand.Options;
                next = 1;
            }

            string invocation = subcommandName == null ? $"/{definition.Name}" : $"/{definition.Name} {subcommandName}";
            string usage = $"Usage: `{$"{invocation} {Usage(options)}".TrimEnd()}`";
            var values = new Dictionary<string, object>();
            for (int i = 0; i < options.Count && next < words.Count; i++)
            {
                CommandOption option = options[i];
                string raw;
                if (i == options.Count - 1 && option.Type == CommandOptionType.String)
                {
                    raw = text[words[next].Index..].TrimEnd();
                    next = words.Count;
                }
                else
                {
                    raw = words[next++].Value;
                }

                if (!TryConvert(option, raw, out object value, out error))
                {
                    error = $"{error} {usage}";
                    return false;
                }
                values[option.Name] = value;
            }

            if (next < words.Count)
            {
                error = $"Too many arguments, starting at `{words[next].Value}`. {usage}";
                return false;
            }

            CommandOption missing = options.FirstOrDefault(x => x.Required && !values.ContainsKey(x.Name));
            if (missing != default)
            {
                error = $"`{missing.Name}` is required. {usage}";
                return false;
            }

            parsed = new ParsedArguments(subcommandName, values);
            return true;
        }

        /// <summary>
        /// Lists <paramref name="options"/> in order, required ones as <c>&lt;name&gt;</c> and optional ones as <c>[name]</c>.
        /// </summary>
        public static string Usage(IReadOnlyList<CommandOption> options)
            => string.Join(' ', options.Select(x => x.Required ? $"<{x.Name}>" : $"[{x.Name}]"));

        private static bool TryConvert(CommandOption option, string raw, out object value, out string error)
        {
            value = default;
            error = default;
            switch (option.Type)
            {
                case CommandOptionType.Integer when long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer):
                    value = integer;
                    return true;

                case CommandOptionType.Integer:
                    error = $"`{option.Name}` must be a whole number, but was `{raw}`.";
                    return false;

                case CommandOptionType.Number when double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double number):
                    value = number;
                    return true;

                case CommandOptionType.Number:
                    error = $"`{option.Name}` must be a number, but was `{raw}`.";
                    return false;

                case CommandOptionType.String when option.Choices.Count > 0:
                    string choice = option.Choices.FirstOrDefault(x => string.Equals(x, raw, StringComparison.OrdinalIgnoreCase));
                    if (choice == default)
                    {
                        error = $"`{option.Name}` must be one of {string.Join(", ", option.Choices.Select(x => $"`{x}`"))}, but was `{raw}`.";
                        return false;
                    }
                    value = choice;
                    return true;

                default:
                    value = raw;
                    return true;
            }
        }

        [GeneratedRegex(@"\S+")]
        private static partial Regex Word();
    }

    /// <summary>
    /// The result of <see cref="TextArgumentParser.TryParse"/>: the canonical subcommand name, if any, and each given option's value keyed by option name.
    /// </summary>
    public sealed record ParsedArguments(string Subcommand, IReadOnlyDictionary<string, object> Values);
}
