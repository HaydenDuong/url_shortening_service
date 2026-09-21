using Microsoft.EntityFrameworkCore;
using url_shortening_service.Data;
using System.Threading.RateLimiting;
using url_shortening_service.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddHostedService<ExpiredShortUrlCleanupService>();

builder.Services.AddHostedService<ShortUrlAnalyticsConsumerService>();

builder.Services.AddScoped<ShortUrlAnalyticsPublisher>();

builder.Services.AddOpenApi();

builder.Services.AddStackExchangeRedisCache(options =>
{
   options.Configuration = builder.Configuration.GetConnectionString("Redis"); 
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        
        RateLimitPartition.GetFixedWindowLimiter(
            
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",

            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,

                Window = TimeSpan.FromMinutes(1),

                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,

                QueueLimit = 0
            }
        )
    );
});

builder.Services.AddDbContext<AppDbContext>(options => 
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"));
});


var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!builder.Configuration.GetValue<bool>("DOTNET_RUNNING_IN_CONTAINER"))
{
    app.UseHttpsRedirection();
}

app.UseRateLimiter();

app.UseAuthorization();

app.MapControllers();

app.Run();
