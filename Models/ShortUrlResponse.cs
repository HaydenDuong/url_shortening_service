namespace url_shortening_service.Models;

public class ShortUrlResponse
{
    public int Id { get; set; }
    public required string Url { get; set; }
    public required string ShortCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}