// This service knows how to send a "/Models/ShortUrlAccessedEvent" message into RabbitMQ
// The "/Controllers/..." should not know RabbitMQ details like:
//      connection.
//      channel.
//      queue declare.
//      JSON bytes.
//      publish.
// The "/Controllers/..." should only say: Publish the event where this shortCode was accessed.
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using url_shortening_service.Models;

namespace url_shortening_service.Services;

public class ShortUrlAnalyticsPublisher
{
    // This is RabbitMQ queue name.
    // both publisher & consumer will use this same queue.
    private const string QueueName = "short-url-analytics";
    
    // This DI object & the constructor parameter "IConfiguration configuration" = ASP.NET Core, please give this service access to the app's configuration system.
    // IConfiguration is the object that lets those code calling it to read the config values from places like:
    //      appsetting.json
    //      appsetting.Development.json
    //      environment variables
    //      Docker Compose environment values
    //      command-line arguments
    //      user secrets (sometimes)
    // ASP.NET Core automatically registers "IConfiguration" in DI when the app starts
    // In "Program.cs": var builder = WebApplication.CreateBuilder(args); = create the app builder & loads configuration.
    // The chain of actions:
    // Docker Compose reads ".env":
    //      -> Docker Compose injects environment variables into the API container.
    //      -> ASP.NET Core reads environment variable when app starts.
    //      -> IConfiguration stores them.
    //      -> GetConnectionString("RabbitMq") retrieves the value
    private readonly IConfiguration _configuration;
    private readonly ILogger<ShortUrlAnalyticsPublisher> _logger;

    // Constructor
    public ShortUrlAnalyticsPublisher(
        IConfiguration configuration,
        ILogger<ShortUrlAnalyticsPublisher> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    // This version opens a RabbitMQ connection each time an event is published.
    // A more production-style version would reuse a long-lived connection / channel.
    public async Task PublishAccessedAsync(string shortCode, DateTime accessedAt)
    {
        // This reads as: "ConnectionStrings:RabbitMq"
        // In Docker Compose, that comes from: "ConnectionStrings__RabbitMq: "${RABBITMQ_CONNECTION_STRING}"
        // So values from ".env" flows into this application
        // This line of code find the value "RabbitMq" by looking for: "ConnectionString:RabbitMq"
        //      In JSON files (e.g: appsetting.json, ...):
        //              {
        //                  "ConnectionStrings": {
        //                      "RabbitMq": "amqp://guest:guest@rabbitmq:5672/"
        //                  }
        //              }
        //      But in Docker Compose, we wrote the environment variables instead of above: ConnectionStrings__RabbitMq: "${RABBITMQ_CONNECTION_STRING}"
        // ASP.NET Core treats double underscore "__" as a nested ":" => ConnectionStrings__RabbitMq == ConnectionStrings:RabbitMq
        var connectionString = _configuration.GetConnectionString("RabbitMq");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogError("RabbitMQ connection string is missing.");
            return;
        }

        // This prepares RabbitMQ connection settings from "amqp://guest:guest@rabbitmq:5672/"
        var factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString)
        };

        // RabbitMQ uses a connection and a channel
        // Rough mental model:
        //      "connection" = TCP-level connection to RabbitMQ (phone line)
        //      "channel"    = lightweight lane for publising / consuming (conversation happening over the above phone line)
        // "await using" = Keep the RabbitMQ connection open during this method
        //               & When this method is done, close / dispose it cleanly
        // "connection" doing 2 jobs:
        //      1. Creates the channel.
        //      2. Keeps the underlying RabbitMQ network connection alive until publishing finishes.
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        // This makes sure the queue exists.
        // Important: declaring a queue is safe if the same queue already exists with the same settings.
        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,          // The queue survives RabbitMQ restart
            exclusive: false,       // This queue is not locked to only this connection
            autoDelete: false,      // RabbitMQ should not delete the queue automatically when consumers disconnect.
            arguments: null        
        );

        var message = new ShortUrlAccessedEvent
        {
            ShortCode = shortCode,
            AccessedAt = accessedAt
        };

        // Turns C# object into JSON
        var json = JsonSerializer.Serialize(message);

        // RabbitMQ messages are sent as bytes, thus, need to encoding the JSON-form of C# object message
        var body = Encoding.UTF8.GetBytes(json);

        var properties = new BasicProperties
        {
            Persistent = true,                  // Asks RabbitMQ to persist the message
            ContentType = "application/json"
        };

        // the channel only works while the connection is alive
        await channel.BasicPublishAsync(
            exchange: string.Empty,             // Uses RabbitMQ's default exchange.
            routingKey: QueueName,              // With default exchange, the routing key is the queue name => This means: Send this message directly to the "short-url-analytics" queue
            mandatory: false,
            basicProperties: properties,
            body: body
        );

        _logger.LogInformation(
            "Short URL analytics event published. ShortCode: {ShortCode}", shortCode
        );
    }
}