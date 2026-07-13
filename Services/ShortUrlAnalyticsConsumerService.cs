using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using url_shortening_service.Data;
using url_shortening_service.Models;

namespace url_shortening_service.Services;

// This background service runs while the app is live, just like Cleanup Service
// Runs continuously in the background to waits for RabbitMQ messages.
public class ShortUrlAnalyticsConsumerService : BackgroundService
{
    // This must match the one in publisher
    // Because: publisher sends message to "short-url-analytics" and consumer must read from the same Queue
    private const string QueueName = "short-url-analytics";

    // Reads the RabbitMQ Connection String
    private readonly IConfiguration _configuration;

    // Lets the background service create a short-lived dependency injection scope. => Because "AppDbContext" should not live forever
    // Scoped = Create one instance for onen unit of work
    // For controllers, that unit of work is usually = one HTTP Request
    // Consumer is not an HTTP request, but, a long-running service
    // Thus, allowing the background service directly injected into "AppDbContext" = mix lifetimes poorly
    // Long-lived background service holding onto short-lived database context = BAD
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ShortUrlAnalyticsConsumerService> _logger;

    public ShortUrlAnalyticsConsumerService(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<ShortUrlAnalyticsConsumerService> logger)
    {
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = _configuration.GetConnectionString("RabbitMq");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogError("RabbitMQ connection string is missing.");
            return;
        }

        // While this application is not shutting down
        //      -> Consumer starts
        //      -> Consumer connects to RabbitMQ
        //      -> Consumer listens to short-url-analytics queue
        //      -> Message arrives
        //      -> Consumer updates PostgreSQL
        //      -> Consumer acknowledges message
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Prepares RabbitMQ connection settings
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(connectionString)
                };

                await using var connection = await factory.CreateConnectionAsync();
                await using var channel = await connection.CreateChannelAsync();

                // This makes sure the queue exists - same settings as the publisher.
                await channel.QueueDeclareAsync(
                    queue: QueueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,      // Queue should not disappear when the consumer disconnects
                    arguments: null
                );

                // This controls how many unacknowledged messages RabbitMQ can give this consumer at once
                // In RabbitMQ, 0 = unlimited / no limit for this setting
                await channel.BasicQosAsync(
                    prefetchSize: 0,            // How many total bytes of unacknowledged messages can RabbitMQ send this consumer? prefetchSize: 0 = do not use a byte-size limit
                    prefetchCount: 1,           // How many unackowledged messages can RabbitMQ send this consumer?
                    global: false               // Apply this QoS / prefetch rule to this consumer / channel behavior locally not as a global channel-wide rule
                );

                // This is the object that receives messages from RabbitMQ
                // It lets us attach an aync event handler to it (in the following code line)
                var consumer = new AsyncEventingBasicConsumer(channel);

                // Whenever a message arrives, run this async code => this does not mean there is a message inside the queue at the moment.
                // It does not immediately run when the app starts, it registers behavior for later usage.
                // "+=" in C# = attach this function to the event
                // Thus, consumer.ReceivedAsync += async (_, eventArgs) => {...}; = Add this async function to the list of things that should run when Received / Async happens.
                // Could technically be multiple handlers:
                //      consumer.ReceivedAsync += HandlerOne;
                //      consumer.receivedAsync += HandlerTwo;
                //                  ...
                // Notice: async (_, eventArgs) => {...} is a lambda / unnamed function which allow us to need-no wrote a separate method
                // Notice: the general form is async (sender, eventArgs) => {...}; 
                //        Each event give 2 information: sender & eventArgs
                //        We do not care about "sender" so doing nothing upon receiving it => Hence, "_" was used instead
                //        "eventArgs" contains RabbitMQ delivery information:
                //              eventArgs.Body          = The actual message bytes
                //              eventArgs.DeliveryTag   = RabbitMQ's ID for this delivered message, used for ack/nack
                // Remember: This handler only runs after BasicConsumeAsync() below it => Cứ nghĩ là nó là methods / functions trước main() và main() là BasicConsumerAsync()
                consumer.ReceivedAsync += async (_, eventArgs) =>
                {
                    try
                    {
                        // RabbitMQ gives us the body as bytes, from: Publisher did: C# object -> JSON string -> UTF-8 bytes
                        // Consumer reverses that: UTF-8 bytes -> JSON string -> C# object
                        var json = Encoding.UTF8.GetString(eventArgs.Body.ToArray());
                        
                        // JSON string like:
                        // {
                        //      "shortCode": "abc123",
                        //      "accessedAt": "2026-07-13T05:30:00Z" 
                        // }
                        // Will be converted into C# object as follow (figuratively)
                        //      message.ShortCode
                        //      message.AccessedAt
                        var message = JsonSerializer.Deserialize<ShortUrlAccessedEvent>(json);

                        // If the message is malformed, we cannot process it
                        // e.g: {} or { "shortCode": ""}
                        // Cannot nack this bad message and requeue it, because RabbitMQ will redeliver this same broken message again and again
                        if (message is null || string.IsNullOrWhiteSpace(message.ShortCode))
                        {
                            _logger.LogWarning("Received invalid analytics message.");

                            await channel.BasicAckAsync(
                                deliveryTag: eventArgs.DeliveryTag,
                                multiple: false
                            );

                            return;
                        }

                        // For each message, we create a small DI scope
                        // That gives the worker a fresh database context for processing that one message.
                        // Essentially:
                        //      Start processing one message.
                        //      Create a small DI scope.
                        //      Get a fresh AppDbContext.
                        //      Update database.
                        //      Dispose the scope.
                        //      Dispose the AppDbContext.
                        using var scope = _scopeFactory.CreateScope();
                        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                        await context.ShortUrls
                            .Where(s => s.ShortCode == message.ShortCode)
                            .ExecuteUpdateAsync(setters => setters
                                .SetProperty(s => s.AccessCount, s => s.AccessCount + 1)
                                .SetProperty(s => s.LastAccessedAt, s => message.AccessedAt));
                        
                        // This tells RabbitMQ: this message was processed successfully & you can now remove it from the queue.
                        await channel.BasicAckAsync(
                            deliveryTag: eventArgs.DeliveryTag,
                            multiple: false
                        );

                        _logger.LogInformation(
                            "Short URL analytics event consumed. ShortCode: {ShortCode}", message.ShortCode
                        );
                    }
                    // If something fails while processing a message like:
                    //      JSON parse fails unexpectedly
                    //      PostgreSQL update fails
                    //      DB connection drops
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Failed to process short URL analytics event."
                        );

                        await channel.BasicNackAsync(
                            deliveryTag: eventArgs.DeliveryTag,
                            multiple: false,
                            requeue: true
                        );
                    }
                };

                // This tells RabbitMQ: start delivering messages from this queue to this consumer
                await channel.BasicConsumeAsync(
                    queue: QueueName,
                    autoAck: false,         // Do not automatically delete messages after delivery => Wait for my BasicAckAsync.
                    consumer: consumer
                );

                _logger.LogInformation("Short URL analytics consumer started.");

                // This line keeps the background service alive after starting the consumer and wait forever until the app shuts down
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // App is shutting down.
            }

            // This gives the consumer retry behavior, because:
            // The app may starts before RabbitMQ container is ready => Consumer may fail first
            // Like this:
            //      API starts
            //      RabbitMQ container is starting but not ready
            //      consumer tries to connect
            //      connection fails
            //      consumer waits 5 seconds
            //      consumer tries again
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "RabbitMQ analytics consumer failed. Retrying soon."
                );

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}