namespace po.Chat
{
    /// <summary>
    /// A message already posted in a channel, with only what the nuke commands need to decide whether to delete it.
    /// </summary>
    /// <param name="FromBot">Whether a bot or an integration such as a webhook posted it, rather than a person.</param>
    public sealed record ChannelMessage(string Id, DateTimeOffset PostedAt, bool FromBot);
}
