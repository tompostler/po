using po.Chat;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace po.Models
{
    public sealed class SlashCommandChannel
    {
        [Required]
        [MaxLength(32)]
        public string SlashCommandName { get; set; }

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

        [MaxLength(64)]
        public string RegistrationData { get; set; }

        public override string ToString() => $"{nameof(SlashCommandChannel)}: {this.SlashCommandName} in {this.Channel}";
    }
}
