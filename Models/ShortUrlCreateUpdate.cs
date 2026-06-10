using System.ComponentModel.DataAnnotations;

namespace url_shortening_service.Models;

public class ShortUrlCreateUpdate
{
    // Required input value - not empty or whitespace
    [Required]

    // Checking if the input URL is valid URL or not
    [Url]
    public string Url { get; set; } = string.Empty;
}
