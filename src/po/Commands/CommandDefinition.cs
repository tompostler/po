namespace po.Commands
{
    /// <summary>
    /// Describes a command independent of the platform that registers it, and doubles as the grammar <see cref="TextArgumentParser"/> parses its arguments with.
    /// A command has either <see cref="Options"/> or <see cref="Subcommands"/>, never both, so the first word is unambiguously one or the other.
    /// </summary>
    public sealed record CommandDefinition
    {
        public required string Name { get; init; }

        public required string Description { get; init; }

        /// <summary>
        /// Whether the command only runs in channels where <c>/commandlist add</c> has enabled it.
        /// </summary>
        public bool RequiresChannelEnablement { get; init; }

        public IReadOnlyList<CommandOption> Options { get; init; } = [];

        public IReadOnlyList<SubcommandDefinition> Subcommands { get; init; } = [];
    }

    public sealed record SubcommandDefinition
    {
        public required string Name { get; init; }

        public required string Description { get; init; }

        public IReadOnlyList<CommandOption> Options { get; init; } = [];
    }

    public sealed record CommandOption
    {
        public required string Name { get; init; }

        public required string Description { get; init; }

        public required CommandOptionType Type { get; init; }

        public bool Required { get; init; }

        /// <summary>
        /// The only values allowed, or empty for any value.
        /// </summary>
        public IReadOnlyList<string> Choices { get; init; } = [];
    }

    /// <summary>
    /// The value types an option can have, which <see cref="CommandContext"/> returns as <see cref="string"/>, <see cref="long"/> and <see cref="double"/>.
    /// </summary>
    public enum CommandOptionType
    {
        String,
        Integer,
        Number,
    }
}
