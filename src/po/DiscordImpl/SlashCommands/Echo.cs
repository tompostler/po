using Discord;
using Discord.WebSocket;
using po.Models;

namespace po.DiscordImpl.SlashCommands
{
    public sealed class Echo : SlashCommandBase
    {
        public override SlashCommand ExpectedCommand => new()
        {
            Name = "echo",
            Version = 1,
            IsGuildLevel = true
        };

        public override SlashCommandProperties BuiltCommand => new SlashCommandBuilder()
            .WithName(this.ExpectedCommand.Name)
            .WithDescription("Returns what you said back to you.")
            .AddOption("text", ApplicationCommandOptionType.String, "The text to echo back.", isRequired: false)
            .Build();

        public override async Task HandleCommandAsync(SocketSlashCommand payload)
            => await payload.RespondAsync(payload.Data.Options.FirstOrDefault()?.Value as string ?? "You apparently have nothing for me to say");
    }
}
