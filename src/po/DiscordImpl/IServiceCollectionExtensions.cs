﻿using Microsoft.Extensions.DependencyInjection;

namespace po.DiscordImpl
{
    public static class IServiceCollectionExtensions
    {
        public static IServiceCollection AddDiscordBotSlashCommands(this IServiceCollection services)
        {
            return services
                .AddSingleton<SlashCommands.SlashCommandBase, SlashCommands.CommandList>()
                .AddSingleton<SlashCommands.SlashCommandBase, SlashCommands.Echo>()
                ;
        }
    }
}
