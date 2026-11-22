namespace po.Utilities
{
    public static class DurationParser
    {
        /// <summary>
        /// Parses a duration from user input.
        /// Accepts a number suffixed with 'd', 'h', 'm', or 's' for scale, or a regular <see cref="TimeSpan"/> such as HH:MM:SS.
        /// </summary>
        /// <returns>An error message and a default value on failure, or a null message and the parsed value on success.</returns>
        public static (string msg, TimeSpan parsed) Parse(string source)
        {
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
            else if (source.Contains('s'))
            {
                return double.TryParse(source.Replace("s", string.Empty), out parsed)
                    ? (null, TimeSpan.FromSeconds(parsed))
                    : ($"Found a `s`, but couldn't parse seconds from '{source}'.", default);
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
