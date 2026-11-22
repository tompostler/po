namespace po.Commands
{
    /// <summary>
    /// A command that runs the same way on every platform, reading its input and replying through <see cref="CommandContext"/>.
    /// </summary>
    public interface ICommand
    {
        CommandDefinition Definition { get; }

        Task HandleAsync(CommandContext context);
    }
}
