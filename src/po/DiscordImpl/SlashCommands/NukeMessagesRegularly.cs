using Discord;
using Discord.WebSocket;
using po.Models;
using System;
using System.Threading.Tasks;

namespace po.DiscordImpl.SlashCommands
{
    public sealed class NukeMessagesRegularly : SlashCommandBase
    {
        public override SlashCommand ExpectedCommand => new()
        {
            Name = "nuke-messages-older-than-day",
            Version = 1,
            IsGuildLevel = true,
            RequiresChannelEnablement = true
        };

        public override SlashCommandProperties BuiltCommand => new SlashCommandBuilder()
            .WithName(this.ExpectedCommand.Name)
            .WithDescription("Removes all messages from the channel older than a day periodically. Note: requires enablement.")
            .Build();

        public override async Task HandleCommandAsync(SocketSlashCommand payload)
        {
            DateTimeOffset dayAgo = DateTimeOffset.UtcNow.AddDays(-1);
            await payload.RespondAsync($"Starting purge of all messages in channel older than {dayAgo:u}.");
            IMessageChannel channel = await payload.GetChannelAsync();
            foreach (IMessage message in await channel.GetMessagesAsync().FlattenAsync())
            {
                if (message.Timestamp < dayAgo)
                {
                    await channel.DeleteMessageAsync(message);
                }
            }
            _ = await channel.SendMessageAsync($"Purged all messages in channel older than {dayAgo:u}.");
        }
    }
}
