using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace po.DataAccess
{
    public sealed class PoContext : DbContext
    {
        private readonly ILoggerFactory loggerFactory;

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
            base.OnModelCreating(modelBuilder);
        }
    }
}
