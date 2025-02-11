﻿namespace po.DiscordImpl
{
    public static class IServiceCollectionExtensions
    {
        public static IServiceCollection AddDiscordBotSlashCommands(this IServiceCollection services)
        {
            return services
                .AddSingleton<SlashCommands.SlashCommandBase, SlashCommands.CommandList>()
                .AddSingleton<SlashCommands.SlashCommandBase, SlashCommands.Echo>()
                .AddSingleton<SlashCommands.SlashCommandBase, SlashCommands.NukeBotMessages>()
                .AddSingleton<SlashCommands.SlashCommandBase, SlashCommands.NukeMessages>()
                .AddSingleton<SlashCommands.SlashCommandBase, SlashCommands.PoCommand>()
                .AddSingleton<SlashCommands.SlashCommandBase, SlashCommands.PoConfigure>()
                .AddSingleton<SlashCommands.SlashCommandBase, SlashCommands.ScheduleMessage>()
                ;
        }
    }
}
