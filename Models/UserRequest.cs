using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace BlinkChatBackend.Models;

public class UserRequest
{
    public string Question { get; set; } = string.Empty;

    [Required]
    public required string SessionId { get; set; }

    [DefaultValue(false)]
    public bool Regenerate { get; set; }

    [DefaultValue(false)]
    public bool Reset { get; set; }
}
