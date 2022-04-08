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
            foreach (IMessage message in await channel.GetMessagesAsync().FlattenAsync())
            {
                await channel.DeleteMessageAsync(message);
            }
            _ = await channel.SendMessageAsync("Purged all messages in channel.");
        }
    }
}
