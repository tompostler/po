using Discord.WebSocket;
using System.Threading.Tasks;

namespace po.DiscordImpl.SlashCommands
{
    public abstract class SlashCommandBase
    {
        public abstract Models.SlashCommand ExpectedCommand { get; }
        public abstract Discord.SlashCommandProperties BuiltCommand { get; }

        public abstract Task HandleCommandAsync(SocketSlashCommand payload);
    }
}
