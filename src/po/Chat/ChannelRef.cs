namespace po.Chat
{
    /// <summary>
    /// Identifies a channel on a specific chat platform.
    /// The string form is <c>platform:id</c>, such as <c>mattermost:4xp9fdt7pbgium38k5k6w95oex</c>.
    /// </summary>
    public readonly record struct ChannelRef(string Platform, string Id)
    {
        public override string ToString() => $"{this.Platform}:{this.Id}";

        /// <summary>
        /// Parses the <c>platform:id</c> form produced by <see cref="ToString"/>.
        /// </summary>
        public static ChannelRef Parse(string value)
        {
            int separator = value?.IndexOf(':') ?? -1;
            if (separator <= 0 || separator == value.Length - 1)
            {
                throw new FormatException($"Channel reference '{value}' is not in the 'platform:id' form.");
            }

            return new ChannelRef(value[..separator], value[(separator + 1)..]);
        }
    }
}
