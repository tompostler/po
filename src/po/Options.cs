namespace po
{
    public static class Options
    {
        public sealed class Discord
        {
            public string BotToken { get; set; }
        }
        public sealed class Sql
        {
            public string ConnectionString { get; set; }
        }
    }
}
