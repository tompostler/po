using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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
                .ComplexProperty(x => x.Channel, MapChannel);

            _ = modelBuilder.Entity<ScheduledBlob>()
                .Property(x => x.Id)
                .HasDefaultValueSql("NEXT VALUE FOR dbo.ScheduledBlobIds");

            _ = modelBuilder.Entity<ScheduledBlob>()
                .Property(x => x.CreatedDate)
                .HasDefaultValueSql("SYSDATETIMEOFFSET()");

            _ = modelBuilder.HasSequence<long>("ScheduledMessageIds").StartsAt(10);

            _ = modelBuilder.Entity<ScheduledMessage>()
                .ComplexProperty(x => x.Channel, MapChannel);

            _ = modelBuilder.Entity<ScheduledMessage>()
                .Property(x => x.Id)
                .HasDefaultValueSql("NEXT VALUE FOR dbo.ScheduledMessageIds");

            _ = modelBuilder.Entity<ScheduledMessage>()
                .Property(x => x.CreatedDate)
                .HasDefaultValueSql("SYSDATETIMEOFFSET()");

            _ = modelBuilder.Entity<SlashCommand>()
                .HasKey(x => x.Name);

            _ = modelBuilder.Entity<SlashCommandChannel>()
                .HasKey(x => new { x.SlashCommandName, x.Platform, x.ChannelId });

            _ = modelBuilder.Entity<SlashCommandChannel>()
                .Property(x => x.RegistrationDate)
                .HasDefaultValueSql("SYSDATETIMEOFFSET()");

            _ = modelBuilder.Entity<SynchronizedBackgroundService>()
                .HasKey(x => x.Name);

            base.OnModelCreating(modelBuilder);
        }

        /// <summary>
        /// Maps a <see cref="Chat.ChannelRef"/> to the same <c>Platform</c> and <c>ChannelId</c> columns that <see cref="SlashCommandChannel"/> declares directly.
        /// </summary>
        private static void MapChannel(ComplexPropertyBuilder<Chat.ChannelRef> channel)
        {
            _ = channel.Property(x => x.Platform).HasColumnName("Platform").HasMaxLength(16).IsRequired();
            _ = channel.Property(x => x.Id).HasColumnName("ChannelId").HasMaxLength(64).IsRequired();
        }
    }
}
