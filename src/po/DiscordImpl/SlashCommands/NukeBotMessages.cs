using Discord;
using Discord.WebSocket;
using po.Models;

namespace po.DiscordImpl.SlashCommands
{
    public sealed class NukeBotMessages : SlashCommandBase
    {
        public override SlashCommand ExpectedCommand => new()
        {
            Name = "nuke-bot-messages",
            Version = 1,
            IsGuildLevel = true,
            RequiresChannelEnablement = true
        };

        public override SlashCommandProperties BuiltCommand => new SlashCommandBuilder()
            .WithName(this.ExpectedCommand.Name)
            .WithDescription("Removes all bot messages from the channel older than a specified amount (if requested).")
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

            await payload.RespondAsync($"Starting purge of all bot messages in channel older than {daysAgo} days.", ephemeral: true);
            IMessageChannel channel = await payload.GetChannelAsync();
            uint count = 0;
            foreach (IMessage message in await channel.GetMessagesAsync().FlattenAsync())
            {
                if (message.Timestamp < DateTimeOffset.UtcNow.AddDays(-daysAgo))
                {
                    if (message.Author.IsBot)
                    {
                        await channel.DeleteMessageAsync(message);
                        count++;
                    }
                }
            }
            _ = await channel.SendMessageAsync($"Removed {count} old bot messages in channel.");
        }
    }
}
