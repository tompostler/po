using po.Chat;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace po.Models
{
    /// <summary>
    /// A command enabled in a channel, and for <c>po</c>, the image container the channel is associated with.
    /// </summary>
    public sealed class CommandChannel
    {
        [Required]
        [MaxLength(32)]
        public string CommandName { get; set; }

        [Required]
        [MaxLength(16)]
        public string Platform { get; set; }

        [Required]
        [MaxLength(64)]
        public string ChannelId { get; set; }

        /// <summary>
        /// <see cref="Platform"/> and <see cref="ChannelId"/> together, for code outside queries.
        /// </summary>
        [NotMapped]
        public ChannelRef Channel
        {
            get => new(this.Platform, this.ChannelId);
            set => (this.Platform, this.ChannelId) = (value.Platform, value.Id);
        }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTimeOffset RegistrationDate { get; set; }

        /// <summary>
        /// The associated container for <c>po</c>, and unused for other commands.
        /// </summary>
        [MaxLength(64)]
        public string RegistrationData { get; set; }

        public override string ToString() => $"{nameof(CommandChannel)}: {this.CommandName} in {this.Channel}";
    }
}
