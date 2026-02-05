using Microsoft.EntityFrameworkCore;
using po.Models;

namespace po.DataAccess
{
    public sealed class PoContext : DbContext
    {
        private readonly ILoggerFactory loggerFactory;

        public DbSet<PoBlob> Blobs { get; set; }
        public DbSet<RandomMessage> RandomMessages { get; set; }
        public DbSet<ScheduledBlob> ScheduledBlobs { get; set; }
        public DbSet<ScheduledMessage> ScheduledMessages { get; set; }
        public DbSet<SlashCommand> SlashCommands { get; set; }
        public DbSet<SlashCommandChannel> SlashCommandChannels { get; set; }
        public DbSet<SynchronizedBackgroundService> SynchronizedBackgroundServices { get; set; }

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
            _ = modelBuilder.Entity<PoBlob>()
                .HasKey(x => new { x.AccountName, x.ContainerName, x.Name });

            _ = modelBuilder.HasSequence<long>("RandomMessageIds").StartsAt(600);

            _ = modelBuilder.Entity<RandomMessage>()
                .Property(x => x.Id)
                .HasDefaultValueSql("NEXT VALUE FOR dbo.RandomMessageIds");

            _ = modelBuilder.Entity<RandomMessage>()
                .Property(x => x.CreatedDate)
                .HasDefaultValueSql("SYSDATETIMEOFFSET()");

            _ = modelBuilder.HasSequence<long>("ScheduledBlobIds").StartsAt(1200);

            _ = modelBuilder.Entity<ScheduledBlob>()
                .Property(x => x.Id)
                .HasDefaultValueSql("NEXT VALUE FOR dbo.ScheduledBlobIds");

            _ = modelBuilder.Entity<ScheduledBlob>()
                .Property(x => x.CreatedDate)
                .HasDefaultValueSql("SYSDATETIMEOFFSET()");

            _ = modelBuilder.HasSequence<long>("ScheduledMessageIds").StartsAt(10);

            _ = modelBuilder.Entity<ScheduledMessage>()
                .Property(x => x.Id)
                .HasDefaultValueSql("NEXT VALUE FOR dbo.ScheduledMessageIds");

            _ = modelBuilder.Entity<ScheduledMessage>()
                .Property(x => x.CreatedDate)
                .HasDefaultValueSql("SYSDATETIMEOFFSET()");

            _ = modelBuilder.Entity<SlashCommand>()
                .HasKey(x => x.Name);

            _ = modelBuilder.Entity<SlashCommandChannel>()
                .HasKey(x => new { x.SlashCommandName, x.ChannelId });

            _ = modelBuilder.Entity<SlashCommandChannel>()
                .Property(x => x.RegistrationDate)
                .HasDefaultValueSql("SYSDATETIMEOFFSET()");

            _ = modelBuilder.Entity<SynchronizedBackgroundService>()
                .HasKey(x => x.Name);

            base.OnModelCreating(modelBuilder);
        }
    }
}
