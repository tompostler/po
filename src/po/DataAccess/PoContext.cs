using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using po.Models;

namespace po.DataAccess
{
    public sealed class PoContext : DbContext
    {
        private readonly ILoggerFactory loggerFactory;

        public DbSet<SlashCommand> SlashCommands { get; set; }
        public DbSet<SlashCommandChannel> SlashCommandChannels { get; set; }

        public PoContext(DbContextOptions<PoContext> dbContextOptions, ILoggerFactory loggerFactory)
            : base(dbContextOptions)
        {
            this.loggerFactory = loggerFactory;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            _ = optionsBuilder
                .UseLoggerFactory(this.loggerFactory)
                // Since it's just Po, we can include the actual values of the parameters to all queries
                .EnableSensitiveDataLogging();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            _ = modelBuilder.Entity<SlashCommand>()
                .HasKey(x => x.Name);

            _ = modelBuilder.Entity<SlashCommandChannel>()
                .HasKey(x => new { x.SlashCommandName, x.GuildId, x.ChannelId });

            _ = modelBuilder.Entity<SlashCommandChannel>()
                .Property(x => x.RegistrationDate)
                .HasDefaultValueSql("SYSDATETIMEOFFSET()");

            base.OnModelCreating(modelBuilder);
        }
    }
}
