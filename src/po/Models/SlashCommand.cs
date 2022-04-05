using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace po.Models
{
    public sealed class SlashCommand
    {
        [Required]
        [MaxLength(32)]
        public string Name { get; set; }

        public bool IsGuildLevel { get; set; }

        public DateTimeOffset? SuccessfullyRegistered { get; set; }

        public bool RequiresChannelEnablement { get; set; }

        public IList<SlashCommandChannel> EnabledChannels { get; set; }
    }
}
