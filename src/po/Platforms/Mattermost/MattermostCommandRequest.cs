using Microsoft.AspNetCore.Mvc;

namespace po.Platforms.Mattermost
{
    /// <summary>
    /// The form Mattermost POSTs to the command callback when someone runs one of po's slash commands, holding only the fields po uses.
    /// </summary>
    public sealed class MattermostCommandRequest
    {
        /// <summary>
        /// The registered command's token, which proves the request came from Mattermost.
        /// </summary>
        [FromForm(Name = "token")]
        public string Token { get; set; }

        /// <summary>
        /// The trigger with its slash, such as <c>/po</c>.
        /// </summary>
        [FromForm(Name = "command")]
        public string Command { get; set; }

        /// <summary>
        /// Everything typed after the trigger, which <see cref="Commands.TextArgumentParser"/> parses.
        /// </summary>
        [FromForm(Name = "text")]
        public string Text { get; set; }

        [FromForm(Name = "channel_id")]
        public string ChannelId { get; set; }

        [FromForm(Name = "user_name")]
        public string UserName { get; set; }

        [FromForm(Name = "response_url")]
        public Uri ResponseUrl { get; set; }
    }
}
