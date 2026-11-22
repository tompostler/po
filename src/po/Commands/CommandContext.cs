using po.Chat;
using System.Text;

namespace po.Commands
{
    /// <summary>
    /// One invocation of a command: who ran it, where, with which arguments, and how to reply.
    /// Each platform implements it over its own payload, such as a Discord interaction or a Mattermost slash command request.
    /// </summary>
    public abstract class CommandContext
    {
        public abstract ChannelRef Channel { get; }

        public abstract string UserName { get; }

        /// <summary>
        /// The invoked subcommand's name, or null for a command without subcommands.
        /// </summary>
        public abstract string Subcommand { get; }

        /// <summary>
        /// The longest text reply the platform accepts, in characters.
        /// </summary>
        public abstract int MaxMessageLength { get; }

        public abstract CancellationToken CancellationToken { get; }

        public string GetString(string name) => this.GetOptionValue(name) as string;

        public long? GetInteger(string name) => this.GetOptionValue(name) as long?;

        public double? GetNumber(string name) => this.GetOptionValue(name) as double?;

        /// <summary>
        /// Tells the platform that a reply is coming, for a command that may take longer than Discord's 3-second limit on the first response.
        /// </summary>
        public abstract Task DeferAsync();

        /// <summary>
        /// Replies with <paramref name="text"/>, or with a file named <paramref name="fileName"/> if it's too long for the platform.
        /// Surrounding whitespace and code fences are trimmed from the file's content, and a file reply is never ephemeral.
        /// </summary>
        public async Task ReplyAsync(
            string text,
            bool ephemeral = false,
            string fileName = "message.txt",
            string fileText = "The response was too long, so it has been posted as a file.")
        {
            if (text.Length > this.MaxMessageLength)
            {
                using var content = new MemoryStream(Encoding.UTF8.GetBytes(text.Trim().Trim('`')));
                await this.ReplyWithFileAsync(fileName, content, fileText);
            }
            else
            {
                await this.ReplyTextAsync(text, ephemeral);
            }
        }

        public abstract Task ReplyAsync(ChatMessage message);

        public abstract Task ReplyWithFileAsync(string fileName, Stream content, string text);

        /// <summary>
        /// Returns the option's value as the <see cref="string"/>, <see cref="long"/> or <see cref="double"/> its <see cref="CommandOptionType"/> calls for, or null if it wasn't given.
        /// For a command with subcommands, this reads the invoked subcommand's options.
        /// </summary>
        protected abstract object GetOptionValue(string name);

        /// <summary>
        /// Replies with text that already fits in <see cref="MaxMessageLength"/>.
        /// </summary>
        protected abstract Task ReplyTextAsync(string text, bool ephemeral);
    }
}
