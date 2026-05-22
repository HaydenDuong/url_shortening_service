// The DB row structure of the table 
namespace url_shortening_service.Models;
public class ShortUrlStorage
{
    public int Id { get; set; }
    public string Url { get; set; }
    public string ShortCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int AccessCount { get; set; }
}
