using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace BlinkChatBackend.Models
{
    public class AIPrompt
    {
        public string Query { get; set; } = string.Empty;
        [Required]
        public string SessionId { get; set; }
        [DefaultValue(false)]
        public bool Regenerate { get; set; }
        [DefaultValue(false)]
        public bool Reset { get; set; }
    }
}
