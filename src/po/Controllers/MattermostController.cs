using Microsoft.AspNetCore.Mvc;
using po.Commands;
using po.Platforms.Mattermost;

namespace po.Controllers
{
    /// <summary>
    /// Receives po's slash commands from Mattermost.
    /// </summary>
    /// <remarks>
    /// This doesn't use <see cref="Attributes.ApiKeyOrSignedUrlAuthorizationAttribute"/>, because Mattermost proves each request with the command's own token instead.
    /// Everything that can fail fast is checked before answering, and the command itself runs after the answer, so Mattermost's roughly 3-second limit never applies to it.
    /// </remarks>
    [ApiController]
    [Route("mattermost")]
    public sealed class MattermostController : ControllerBase
    {
        private readonly MattermostCommandTokens tokens;
        private readonly CommandDispatcher dispatcher;
        private readonly MattermostPlatform platform;
        private readonly MattermostApiClient apiClient;
        private readonly IHostApplicationLifetime lifetime;
        private readonly ILogger<MattermostController> logger;

        public MattermostController(
            MattermostCommandTokens tokens,
            CommandDispatcher dispatcher,
            MattermostPlatform platform,
            MattermostApiClient apiClient,
            IHostApplicationLifetime lifetime,
            ILogger<MattermostController> logger)
        {
            this.tokens = tokens;
            this.dispatcher = dispatcher;
            this.platform = platform;
            this.apiClient = apiClient;
            this.lifetime = lifetime;
            this.logger = logger;
        }

        [HttpPost("command")]
        [Consumes("application/x-www-form-urlencoded")]
        public IActionResult Command([FromForm] MattermostCommandRequest request)
        {
            string trigger = request.Command?.TrimStart('/');
            if (!this.tokens.IsValid(trigger, request.Token))
            {
                // Mattermost shows the status to the person who ran the command, which is enough to notice a stale registration.
                this.logger.LogWarning($"Rejected a Mattermost command request for `{request.Command}` with an unknown token.");
                return this.Unauthorized();
            }

            if (!this.dispatcher.TryGetCommand(trigger, out ICommand command))
            {
                return Ephemeral($"`/{trigger}` is registered, but po has no handler for it.");
            }

            if (!TextArgumentParser.TryParse(command.Definition, request.Text, out ParsedArguments arguments, out string error))
            {
                return Ephemeral(error);
            }

            var context = new MattermostCommandContext(request, arguments, this.platform, this.apiClient, this.lifetime.ApplicationStopping);
            _ = Task.Run(() => this.dispatcher.DispatchAsync(command, context));

            // An empty body tells Mattermost there's nothing to post; the command replies on its own.
            return this.Ok();
        }

        private static JsonResult Ephemeral(string text)
            => new(
                new MattermostCommandResponse { ResponseType = MattermostCommandResponse.Ephemeral, Text = text },
                MattermostApiClient.JsonOptions);
    }
}
