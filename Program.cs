//   Program.cs                    Request (POST /shorten)
//   AddDbContext ───────────────► DI creates AppDbContext
//        │                              │
//        │ options: InMemory            ▼
//        └────────────────────► Controller uses _context.ShortUrls
//                                        │
//                                        ▼
//                                 SaveChangesAsync() → row stored

using Microsoft.EntityFrameworkCore;
using url_shortening_service.Data;
using System.Threading.RateLimiting;
using url_shortening_service.Services;

var builder = WebApplication.CreateBuilder(args);

// "builder.Services" collection = dependency injection container
// Purpose: ASP.NET Core has this as a central place that knows how to create dependencies for classes in this application to call and use without need to manually create them
// It serves as "service registry", thus, when a controller constructor says:
//      public RedirectController(
//          AppDbContext context,
//          ILogger<RedirectController> logger,
//          IDistributedCache cache)
// ASP.NET sees those and thinks:
//      To create RedirectController, I need:
//              - AppDbContext
//              - ILogger<RedirectController>
//              - IDistributedCache
// It will checks the DI container ("builder.Services") => ASP.NET creates those objects and passes them (injecting) into the constructor

// Add services to the container.
builder.Services.AddControllers();

// This means:
//      When the app starts, create and run ExpiredShortUrlCleanupService as a hosted background service.
//      Whent he app shuts down, ask it to stop cleanly
// "AddHostedService<>" = register this class as a background worker managed by the ASP.Net host.
// "Host" is the thing running this whole application:
//      Starts Kestrel.
//      Loads configuration.
//      Sets up dependency injection.
//      Starts controllers.
//      Starts hosted services.
//      Handles shutdown.
// After this registration, startup becomes roughly:
//      1. App starts.
//      2. DI container is built
//      3. Kestrel starts listening for HTTP requests.
//      4. "ExpiredShortUrlCleanupService" starts running.
//      5. The cleanup loop begins.
builder.Services.AddHostedService<ExpiredShortUrlCleanupService>();

builder.Services.AddHostedService<ShortUrlAnalyticsConsumerService>();

// This tells ASP.NET Core's DI container: If some class asks for ShortUrlAnalyticsPublisher => create one and give it to that class
// "AddScoped" is used, because, this app's controllers are request-scoped => ASP.NET creates controller-related services per HTTP request
// Thus, "AddScoped<ShortUrlAnalyticsPublisher>()" = create one publisher instance for the curernt HTTP request scope.
//          Resuse it within that same request if needed.
//          Dispose it when the request ends.
builder.Services.AddScoped<ShortUrlAnalyticsPublisher>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Register IDistributedCache
// Use Redis as the implementation
// Get Redis connection string from configuration
// What these truly mean:
//      When something asks for IDistributeCache,
//      Use Redis, and connect to Redis-container on Docker through ConnectionStrings:Redis:
//          - Local = come from "appsetting.json"
//          - Docker container = come from Docker/docker-compose.yml 
builder.Services.AddStackExchangeRedisCache(options =>
{
   options.Configuration = builder.Configuration.GetConnectionString("Redis"); 
});

// Register the Rate Limiter Service
// Add Rate Limiting Services to Dependency Injection.
// Configure how requests should be limited.
builder.Services.AddRateLimiter(options =>
{
    // When a request is blocked by the rate limiter, return HTTP 429.
    // "429 Too Many Requests" = standard HTTP status for rate limiting.
    // If a client sends too many request => they will receive "HTTP/1.1 429 Too Many Requests"
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // "options.GlobalLimiter = ..." : Apply this limiter to all requests / endpoints in this app by default
    // "PartitionedRateLimiter.Create<HttpContext, string>(httpContext => ...) = Create a rate limiter that can separate clients into different groups
    // Those different groups are called partitions.
    // By normal convention, no user is allow to consume the entire app's limit.
    // E.g: 
    //      Bad: Whole API allows 10 requests / minute total
    //           One user sends 10 requests => Everyone else gets blocked.
    //      Better: Each IP gets 10 requests / minute
    // Partitioning lets every client get their own bucket / window.
    // "HttpContext" = use information from the current HTTP Request
    // "string" = the partition key will be a string => In this case, the string is the IP address of the client
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        // This creates a fixed-window limiter for each partition.
        RateLimitPartition.GetFixedWindowLimiter(
            // Extract the IP address from the current HTTP request and convert it into string, if the IP Address is null then use "unknown" as fallback
            // this will not causing the app to crash if IP address is missing.
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",

            // The factor creates limiter settings for each partition
            // In C#, "_" is often used as a variable name when: "A value is passed to us, but we do not care about it"
            // In this case, for any partition / client, use these same fixed window settings in the followings
            // In fact, this can be written as: factory: partitionKey => new FixedWindowRateLimiterOptions{...}
            // Remember, since we do not make any exclusion setting for an IP address => "_" is being used in here
            factory: _ => new FixedWindowRateLimiterOptions
            {
                // Allow 10 requests per window.
                PermitLimit = 10,

                // The window length is 1 minute => 10 requests per minute.
                Window = TimeSpan.FromMinutes(1),

                
                // If requets were queued, process older requests first.
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,

                // Rate Limiters can optionally queue extra requests instead of rejecting them immediately.
                // Since QueueLimit is set to 0, so extra requests are rejected instead of queued.
                // Do not queue extra requests => Reject immediately with Http 429
                // This is required parameter by "FixedWindowRateLimiterOptions", thus, it must be included
                QueueLimit = 0
            }
        )
    );
});

// Tell DI: "When someone needs AppDbContext, build it like this."

// UseInMemoryDatabase = fake DB in RAM (data gone when app stops — fine for learning).
// Later swap to UseNpgsql(connectionString) for real Postgres — AppDbContext file stays same.
// "UrlShortenerDB" is a database name
// builder.Services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase("UrlShortenerDB"));

// Using PostgreSQL instead of RAM
// "builder.Configuration.GetConnectionString("Postgres")" = Asks ASP.NET Core to give the final value of ConnectionString:Postgres after all config sources are combined
// Locally, the final value comes from: "appsettings.json"
// In Docker Compose, the final value comes from: "Docker/docker-compose.yml - ConnectionStrings__Postgres"
// As result, this latter (environment variables) override JSON config
// Nghĩa là nếu chạy locally trên máy tính mà ko thông qua API container trên Docker thì app sẽ kết nối với DB-container trên Docker thông qua giá trị được lưu trong appsetting.json
// Còn cái app trong API container trên Docker, thì sẽ dùng giá trị trong Docker/docker-compose.yml
builder.Services.AddDbContext<AppDbContext>(options => 
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"));
});


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Middleware Order:
// HTTPS Redirection -> Rate Limiter -> Authorization (Not yet implemented) -> Controllers

// When running inside Docker, skip HTTPS redirection for now
// Else, keep HTTPS redirection when running normally on local machine instead of Docker
// .NET containers automatically set "DOTNET_RUNNING_IN_CONTAINER=true" 
// In production, HTTPS us often handled by something in front of the app:
//      Reverse Proxy
//      Load Balancer
//      API Gateway
//      Nginx
//      Cloud Service
// The app container often receives plains HTTP internally, while HTTPS is handled at the edge
if (!builder.Configuration.GetValue<bool>("DOTNET_RUNNING_IN_CONTAINER"))
{
    app.UseHttpsRedirection();
}

app.UseRateLimiter();

app.UseAuthorization();

app.MapControllers();

app.Run();
