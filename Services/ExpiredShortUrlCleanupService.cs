// This imports "BackgroundService"
// It is a part of ASP.NET Core hosting.
// A built-in base class for "run something in the background while my app is running."
using Microsoft.Extensions.Hosting;

// "ExecuteDeleteAsync()" is an EF Core extension method. Thus, we must import it
using Microsoft.EntityFrameworkCore;

// "AppDbContext" lives in "/Data" namespace
// We need "AppDbContext" here because the cleanup job must talk to the "ShortUrls" table
// "AppDbContext" is the EF Core doorway into PostgreSQL => without it, the background service has no way to query / delete expired rows.
using url_shortening_service.Data;

namespace url_shortening_service.Services;

// This creates the cleanup worker.
// Using ": BackgroundService" as the parent and "ExpiredShortUrlCleanupService" is inheritance from the base class
public class ExpiredShortUrlCleanupService : BackgroundService
{
    // This field stores the object that can create a new DI scope.
    // Why? Because "AppDbContext" is registered as scoped.
    // In normal HTTP requests, ASP.NET creates the scope for you. 
    // However, a background service is not an HTTP request, thus, we need to create the scope for it manually.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpiredShortUrlCleanupService> _logger;

    // Constructor
    // ASP.NET will create the cleanup background service and pass in:
    // IServiceScopeFactory
    // ILogger<ExpiredShortUrlCleanupService>
    public ExpiredShortUrlCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<ExpiredShortUrlCleanupService> logger)
    {
        // These fields are declared to store the injected objects so other methods in the class can use them.
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // "Protected" = Only this class and its child classes can use it directly.
    // "override" = In fact, ": BackgroundService" already defines this method, thus, we override / custom it with our own logic.
    // "async Task" = this method can use "await", and it represents async work.
    // "CancellationToken stoppingToken" = this is ASP.NET shutdown signal.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // "!stoppingToken.IsCancellationRequested" = mean the app is running normally.
        // Once the app is shuts down, then, a cancellation requested for stoppingToken will be made
        // Without this logic check / kind of condition, the background task might ignore shutdown & make the app slow or messy to stop
        while (!stoppingToken.IsCancellationRequested)
        {
            // This creates a temporary dependency injection scope
            // Think of this:
            //      1. Start one cleanup cycle.
            //      2. Create a small service container for that cycle.
            //      3. Get a fresh "AppDbContext" session.
            // "using" automatically disposes the scope when this loop iteration finishes.
            // Disposing the scope also disposes scoped services created inside it, including AppDbContext.
            using var scope = _scopeFactory.CreateScope();

            // This ask the temporary scope to give it an "AppDbContext" for this cleanup cycle.
            // "GetRequiredService" = if ASP.NET does not know how to create "AppDbContext", throw an error immediately.
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // This starts a query against the "ShortUrls" table
            var deletedCount = await context.ShortUrls
                .Where(s => s.ExpiresAt.HasValue && s.ExpiresAt.Value <= DateTime.UtcNow)

                // tell PostGreSQL to delete all matching rows directly.
                // "stoppingToken" lets the delete operation cooperate with app shutdown
                .ExecuteDeleteAsync(stoppingToken);

            _logger.LogInformation(
                "Expired short URL background cleanup completed. Count: {DeletedCount}", deletedCount
            );

            // Sleep asynchronously for 5 minutes, unless the app is shutting down
            // Thread.Sleep(...) = holds a thread doing nothing.
            // await Task.Delay(...) = gives the thread back to ASP.NET while waiting => Placing "stoppingToken" inside here means the wait can be interrupted:
            // If the app is shutting down, it does not have to wait the full 5 minutes.
            // Current behavior of this skeleton:
            //         App starts       => Background service starts => Loop begins => Wait 5 minutes => Loop repeats => Wait 5 minutes => ...
            //         App shuts down   => stoppingToken is cancelled => Loop exits => Service stops. 
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}