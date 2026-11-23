using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.EntityFrameworkCore;
using po.DataAccess;

namespace po.Commands
{
    /// <summary>
    /// Finds commands by name and runs them, enforcing channel enablement and turning a failure into a reply instead of an exception.
    /// </summary>
    public sealed class CommandDispatcher
    {
        private readonly Dictionary<string, ICommand> commands;
        private readonly IServiceProvider serviceProvider;
        private readonly ILogger<CommandDispatcher> logger;
        private readonly TelemetryClient telemetryClient;

        public CommandDispatcher(
            IEnumerable<ICommand> commands,
            IServiceProvider serviceProvider,
            ILogger<CommandDispatcher> logger,
            TelemetryClient telemetryClient)
        {
            this.commands = commands.ToDictionary(x => x.Definition.Name, StringComparer.OrdinalIgnoreCase);
            this.serviceProvider = serviceProvider;
            this.logger = logger;
            this.telemetryClient = telemetryClient;
        }

        public IEnumerable<CommandDefinition> Definitions => this.commands.Values.Select(x => x.Definition);

        /// <summary>
        /// Finds a command by name, ignoring case, so a platform can parse its arguments against the definition before running it.
        /// </summary>
        public bool TryGetCommand(string name, out ICommand command)
            => this.commands.TryGetValue(name ?? string.Empty, out command);

        /// <summary>
        /// Runs <paramref name="command"/>, and never throws: failures are logged and replied to the channel.
        /// </summary>
        public async Task DispatchAsync(ICommand command, CommandContext context)
        {
            using IOperationHolder<RequestTelemetry> op = this.telemetryClient.StartOperation<RequestTelemetry>($"{this.GetType().FullName}.{command.Definition.Name}");
            this.logger.LogInformation($"Running {command.Definition.Name} {context.Subcommand} for {context.UserName} in {context.Channel}");

            try
            {
                if (command.Definition.RequiresChannelEnablement && !await this.IsEnabledAsync(command.Definition.EnablementGroup, context))
                {
                    await context.ReplyAsync($"`{command.Definition.Name}` is not enabled for the current channel. Try `/commandlist add {command.Definition.EnablementGroup}`.", ephemeral: true);
                    return;
                }

                await command.HandleAsync(context);
            }
            catch (Exception ex) when (!context.CancellationToken.IsCancellationRequested)
            {
                op.Telemetry.Success = false;
                this.logger.LogError(ex, $"Could not handle {command.Definition.Name}.");
                try
                {
                    await context.ReplyAsync($"Caught an exception: {ex}", fileName: "exception.txt");
                }
                catch (Exception replyEx)
                {
                    this.logger.LogError(replyEx, "Could not reply with the exception.");
                }
            }
        }

        private async Task<bool> IsEnabledAsync(string enablementGroup, CommandContext context)
        {
            using IServiceScope scope = this.serviceProvider.CreateScope();
            using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();
            return await poContext.CommandChannels.AnyAsync(
                x => x.CommandName == enablementGroup && x.Platform == context.Channel.Platform && x.ChannelId == context.Channel.Id,
                context.CancellationToken);
        }
    }
}
