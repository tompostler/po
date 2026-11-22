using Microsoft.Extensions.Options;
using po.Commands;

namespace po.Platforms.Mattermost
{
    /// <summary>
    /// Makes the team's slash commands match the <see cref="CommandDispatcher"/>'s definitions once at startup, and hands their tokens to <see cref="MattermostCommandTokens"/>.
    /// </summary>
    /// <remarks>
    /// Only commands the bot created are changed or deleted, so commands people made by hand are left alone, even one that takes a trigger po wants.
    /// Built-in and plugin commands count as taken too, because Mattermost runs them ahead of custom commands, so a custom command with the same trigger would never be reached.
    /// Tokens are only held in memory, and a restart reconciles again, so deleting a command in Mattermost's settings is fixed by restarting po.
    /// </remarks>
    public sealed class MattermostCommandRegistrar : BackgroundService
    {
        private readonly MattermostPlatform platform;
        private readonly MattermostApiClient apiClient;
        private readonly CommandDispatcher dispatcher;
        private readonly MattermostCommandTokens tokens;
        private readonly Options.Mattermost options;
        private readonly ILogger<MattermostCommandRegistrar> logger;

        public MattermostCommandRegistrar(
            MattermostPlatform platform,
            MattermostApiClient apiClient,
            CommandDispatcher dispatcher,
            MattermostCommandTokens tokens,
            IOptions<Options.Mattermost> options,
            ILogger<MattermostCommandRegistrar> logger)
        {
            this.platform = platform;
            this.apiClient = apiClient;
            this.dispatcher = dispatcher;
            this.tokens = tokens;
            this.options = options.Value;
            this.logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (string.IsNullOrEmpty(this.options.TeamName) || this.options.CommandCallbackUri == null)
            {
                this.logger.LogError($"{nameof(Options.Mattermost)}:{nameof(this.options.TeamName)} and {nameof(Options.Mattermost)}:{nameof(this.options.CommandCallbackUri)} must both be configured, so no slash commands are registered.");
                return;
            }

            try
            {
                await this.platform.WaitUntilReadyAsync(stoppingToken);
                await this.ReconcileAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // An unhandled exception here would stop the host.
                this.logger.LogError(ex, "Could not reconcile Mattermost slash commands, so none will be accepted until po restarts.");
            }
        }

        private async Task ReconcileAsync(CancellationToken cancellationToken)
        {
            MattermostUser me = await this.apiClient.GetMeAsync(cancellationToken);
            MattermostTeam team = await this.apiClient.GetTeamByNameAsync(this.options.TeamName, cancellationToken);
            IReadOnlyList<MattermostCommand> custom = await this.apiClient.ListCommandsAsync(team.Id, customOnly: true, cancellationToken);
            IReadOnlyList<MattermostCommand> all = await this.apiClient.ListCommandsAsync(team.Id, customOnly: false, cancellationToken);

            var tokensByTrigger = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (CommandDefinition definition in this.dispatcher.Definitions)
            {
                try
                {
                    MattermostCommand desired = this.ToMattermostCommand(definition, team.Id);
                    MattermostCommand owned = custom.FirstOrDefault(x => x.CreatorId == me.Id && string.Equals(x.Trigger, desired.Trigger, StringComparison.OrdinalIgnoreCase));
                    MattermostCommand registered;
                    if (owned != null && IsSame(owned, desired))
                    {
                        registered = owned;
                    }
                    else if (owned != null)
                    {
                        registered = await this.apiClient.UpdateCommandAsync(owned with
                        {
                            Method = desired.Method,
                            Url = desired.Url,
                            DisplayName = desired.DisplayName,
                            Description = desired.Description,
                            AutoComplete = desired.AutoComplete,
                            AutoCompleteDesc = desired.AutoCompleteDesc,
                            AutoCompleteHint = desired.AutoCompleteHint,
                        }, cancellationToken);
                        this.logger.LogInformation($"Updated Mattermost command /{registered.Trigger}.");
                    }
                    else if (all.Concat(custom).Any(x => string.Equals(x.Trigger, desired.Trigger, StringComparison.OrdinalIgnoreCase)))
                    {
                        this.logger.LogWarning($"Mattermost command /{desired.Trigger} is taken by a built-in, a plugin or someone else's command, so po won't register or answer it.");
                        continue;
                    }
                    else
                    {
                        registered = await this.apiClient.CreateCommandAsync(desired, cancellationToken);
                        this.logger.LogInformation($"Created Mattermost command /{registered.Trigger}.");
                    }

                    tokensByTrigger[registered.Trigger] = string.IsNullOrEmpty(registered.Token)
                        ? await this.apiClient.RegenerateCommandTokenAsync(registered.Id, cancellationToken)
                        : registered.Token;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    this.logger.LogError(ex, $"Could not register Mattermost command /{definition.Name}.");
                }
            }

            foreach (MattermostCommand stale in custom.Where(x => x.CreatorId == me.Id && !this.dispatcher.TryGetCommand(x.Trigger, out _)))
            {
                try
                {
                    await this.apiClient.DeleteCommandAsync(stale.Id, cancellationToken);
                    this.logger.LogInformation($"Deleted Mattermost command /{stale.Trigger}, which po no longer has.");
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    this.logger.LogError(ex, $"Could not delete Mattermost command /{stale.Trigger}.");
                }
            }

            this.tokens.Replace(tokensByTrigger);
            this.logger.LogInformation($"Accepting {tokensByTrigger.Count} Mattermost slash command(s) in team {team.Name}.");
        }

        private MattermostCommand ToMattermostCommand(CommandDefinition definition, string teamId)
        {
            string hint = definition.Subcommands.Count > 0
                ? string.Join(" | ", definition.Subcommands.Select(x => $"{x.Name} {TextArgumentParser.Usage(x.Options)}".TrimEnd()))
                : TextArgumentParser.Usage(definition.Options);

            return new MattermostCommand
            {
                TeamId = teamId,
                Trigger = definition.Name,
                Method = "P",
                Url = this.options.CommandCallbackUri.AbsoluteUri,
                DisplayName = definition.Name,
                Description = definition.Description,
                AutoComplete = true,
                AutoCompleteDesc = definition.Description,
                AutoCompleteHint = hint,
            };
        }

        private static bool IsSame(MattermostCommand existing, MattermostCommand desired)
            => existing.Method == desired.Method
                && existing.Url == desired.Url
                && existing.DisplayName == desired.DisplayName
                && existing.Description == desired.Description
                && existing.AutoComplete == desired.AutoComplete
                && existing.AutoCompleteDesc == desired.AutoCompleteDesc
                && existing.AutoCompleteHint == desired.AutoCompleteHint;
    }
}
