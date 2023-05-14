using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace po.Models
{
    public sealed class ScheduledBlob
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [MaxLength(64)]
        public string ContainerName { get; set; }

        [Required]
        public ulong ChannelId { get; set; }

        [MaxLength(32)]
        public string Category { get; set; }

        [MaxLength(128)]
        public string Username { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTimeOffset CreatedDate { get; set; }

        public DateTimeOffset ScheduledDate { get; set; }
    }
}
