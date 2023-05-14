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
        public ulong ChannelId { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTimeOffset RegistrationDate { get; set; }

        [MaxLength(64)]
        public string RegistrationData { get; set; }

        public override string ToString() => $"{nameof(SlashCommandChannel)}: {this.SlashCommandName} in {this.ChannelId}";
    }
}
