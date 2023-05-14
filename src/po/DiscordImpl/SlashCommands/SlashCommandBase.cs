using Discord.WebSocket;

namespace po.DiscordImpl.SlashCommands
{
    public abstract class SlashCommandBase
    {
        public abstract Models.SlashCommand ExpectedCommand { get; }
        public abstract Discord.SlashCommandProperties BuiltCommand { get; }

        public abstract Task HandleCommandAsync(SocketSlashCommand payload);


        protected static (string msg, TimeSpan parsed) GetTimeSpan(string source)
        {
            // Look for the following formats:
            //  ##d --> number of days
            //  ##h --> number of hours
            //  ##m --> number of minutes
            //  HH:MM:SS --> regular timespan parsing

            source = source.ToLower();
            double parsed;
            if (source.Contains('m'))
            {
                return double.TryParse(source.Replace("m", string.Empty), out parsed)
                    ? (null, TimeSpan.FromMinutes(parsed))
                    : ($"Found a `m`, but couldn't parse minutes from '{source}'.", default);
            }
            else if (source.Contains('h'))
            {
                return double.TryParse(source.Replace("h", string.Empty), out parsed)
                    ? (null, TimeSpan.FromHours(parsed))
                    : ($"Found a `h`, but couldn't parse hours from '{source}'.", default);
            }
            else if (source.Contains('d'))
            {
                return double.TryParse(source.Replace("d", string.Empty), out parsed)
                    ? (null, TimeSpan.FromDays(parsed))
                    : ($"Found a `d`, but couldn't parse days from '{source}'.", default);
            }
            else
            {
                return TimeSpan.TryParse(source, out TimeSpan parsedt)
                    ? (null, parsedt)
                    : ($"Couldn't parse a `TimeSpan` from '{source}'.", default);
            }
        }
    }
}
