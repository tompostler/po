using Microsoft.Extensions.DependencyInjection;

namespace po.DiscordImpl
{
    public static class IServiceCollectionExtensions
    {
        public static IServiceCollection AddDiscordBotSlashCommands(this IServiceCollection services)
        {
            return services
                .AddSingleton<SlashCommands.SlashCommandBase, SlashCommands.CommandList>()
                .AddSingleton<SlashCommands.SlashCommandBase, SlashCommands.Echo>()
                .AddSingleton<SlashCommands.SlashCommandBase, SlashCommands.NukeMessages>()
                .AddSingleton<SlashCommands.SlashCommandBase, SlashCommands.NukeMessagesRegularly>()
                .AddSingleton<SlashCommands.SlashCommandBase, SlashCommands.PoCommand>()
                .AddSingleton<SlashCommands.SlashCommandBase, SlashCommands.PoConfigure>()
                ;
        }
    }
}
