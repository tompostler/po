using Discord;
using Discord.WebSocket;
using po.Models;
using System;
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

        public override Task HandleCommandAsync(SocketSlashCommand payload) => throw new NotImplementedException();
    }
}
