using System.ComponentModel.DataAnnotations;

namespace po.Models
{
    public sealed class SynchronizedBackgroundService
    {
        [Required]
        [MaxLength(128)]
        public string Name { get; set; }

        public DateTimeOffset LastExecuted { get; set; }

        public ulong CountExecutions { get; set; }
    }
}
