using Discord;
using Discord.WebSocket;
using po.Models;

namespace po.DiscordImpl.SlashCommands
{
    public sealed class NukeMessages : SlashCommandBase
    {
        public override SlashCommand ExpectedCommand => new()
        {
            Name = "nuke-messages",
            Version = 2,
            IsGuildLevel = true,
            RequiresChannelEnablement = true
        };

        public override SlashCommandProperties BuiltCommand => new SlashCommandBuilder()
            .WithName(this.ExpectedCommand.Name)
            .WithDescription("Removes all messages from the channel older than a specified amount (if requested).")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("days")
                .WithDescription("The number of days ago to delete messages older than")
                .WithType(ApplicationCommandOptionType.Number)
                .WithRequired(false)
            )
            .Build();

        public override async Task HandleCommandAsync(SocketSlashCommand payload)
        {
            double daysAgo = payload.Data.Options.FirstOrDefault(x => x.Name == "days")?.Value as double? ?? 1;

            await payload.RespondAsync($"Starting purge of all messages in channel older than {daysAgo} days.");
            IMessageChannel channel = await payload.GetChannelAsync();
            uint count = 0;
            foreach (IMessage message in await channel.GetMessagesAsync().FlattenAsync())
            {
                if (message.Timestamp < DateTimeOffset.UtcNow.AddDays(-daysAgo))
                {
                    await channel.DeleteMessageAsync(message);
                    count++;
                }
            }
            _ = await channel.SendMessageAsync($"Purged {count} messages in channel.");
        }
    }
}
