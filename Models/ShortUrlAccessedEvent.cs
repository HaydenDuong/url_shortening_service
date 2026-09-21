namespace url_shortening_service.Models;
public class ShortUrlAccessedEvent
{
    public string ShortCode { get; set;} = string.Empty;
    public DateTime AccessedAt { get; set; }
}