using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace po.Models
{
    public sealed class SlashCommandChannel
    {
        [Required]
        public string SlashCommandName { get; set; }

        [Required]
        public ulong ChannelId { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTimeOffset RegistrationDate { get; set; }

        public override string ToString() => $"{nameof(SlashCommandChannel)}: {this.SlashCommandName} in {this.ChannelId}";
    }
}
