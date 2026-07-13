namespace url_shortening_service.Models;

public class ShortUrlResponseStats
{
    public int Id { get; set; }
    public required string Url { get; set; }
    public required string ShortCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public int AccessCount { get; set; }
}
