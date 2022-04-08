using Discord;
using Discord.WebSocket;
using po.Models;
using System.Threading.Tasks;

namespace po.DiscordImpl.SlashCommands
{
    public sealed class NukeMessages : SlashCommandBase
    {
        public override SlashCommand ExpectedCommand => new()
        {
            Name = "nuke-messages",
            Version = 1,
            IsGuildLevel = true,
            RequiresChannelEnablement = true
        };

        public override SlashCommandProperties BuiltCommand => new SlashCommandBuilder()
            .WithName(this.ExpectedCommand.Name)
            .WithDescription("Removes all messages from the channel. Note: this command requires enablement.")
            .Build();

        public override async Task HandleCommandAsync(SocketSlashCommand payload)
        {
            await payload.RespondAsync("Starting purge of all messages in channel.");
            IMessageChannel channel = await payload.GetChannelAsync();
            uint count = 0;
            foreach (IMessage message in await channel.GetMessagesAsync().FlattenAsync())
            {
                await channel.DeleteMessageAsync(message);
                count++;
            }
            _ = await channel.SendMessageAsync($"Purged {count} messages in channel.");
        }
    }
}
