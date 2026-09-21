namespace url_shortening_service.Models;

// This class is a message contract to be used for RabbitMQ
// It describes the shape of the message that travels through RabbitMQ:
// {
//      "shortCode": "abc123",
//      "accessedAt": "2026-07-13T05:30:00Z"
// }
// Producer & Consumer both need to use this structure:
// Producer:
//          Creates ShortUrlAccessedEvent
//          Serializes it to JSON
//          Sends it to RabbitMQ
// Consumer:
//          Reads JSON from RabbitMQ
//          Deserializes it back into ShortUrlAccessedEvent
//          Updates PostgreSQL
// => This class becomes the agreement between those two pieces of code.
public class ShortUrlAccessedEvent
{
    public string ShortCode { get; set;} = string.Empty;

    // "AccessedAt" is included because the event should record (true access time) when the redirect happened, not when the worker eventually processed it.
    public DateTime AccessedAt { get; set; }
}