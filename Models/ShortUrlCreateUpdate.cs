using System.ComponentModel.DataAnnotations;

namespace url_shortening_service.Models;

public class ShortUrlCreateUpdate
{
    [Required]
    [Url]
    public string Url { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
}
