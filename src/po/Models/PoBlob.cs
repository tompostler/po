using System.ComponentModel.DataAnnotations;

namespace po.Models
{
    public sealed class PoBlob
    {
        [Required]
        [MinLength(3), MaxLength(23)]
        public string AccountName { get; set; }

        [Required]
        [MinLength(3), MaxLength(63)]
        public string ContainerName { get; set; }

        [Required]
        [MinLength(1), MaxLength(1024)]
        public string Name { get; set; }

        [MaxLength(32)]
        public string Category { get; set; }

        public bool Seen { get; set; }

        public DateTimeOffset CreatedOn { get; set; }

        public DateTimeOffset LastModified { get; set; }

        public DateTimeOffset LastSeen { get; set; }

        public long ContentLength { get; set; }

        [StringLength(32)]
        public string ContentHash { get; set; }

        public override string ToString() => $"{this.AccountName}/{this.ContainerName}/{this.Name}";

        public void CopyFrom(PoBlob other)
        {
            // Skip over the AccountName/ContainerName/Name as those are the keys

            this.Category = other.Category;
            this.CreatedOn = other.CreatedOn;
            this.LastModified = other.LastModified;
            this.LastSeen = other.LastSeen;
            this.ContentLength = other.ContentLength;
            this.ContentHash = other.ContentHash;
        }
    }
}
