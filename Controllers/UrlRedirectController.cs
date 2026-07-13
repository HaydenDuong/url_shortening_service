using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using url_shortening_service.Data;
using Microsoft.Extensions.Caching.Distributed;

namespace url_shortening_service.Controllers;

[ApiController]
[Route("go")]
public class RedirectController : ControllerBase
{
    private readonly AppDbContext _context;
    // Added a logger field to inject into the constructor
    private readonly ILogger<RedirectController> _logger;
    private readonly IDistributedCache _cache;

    public RedirectController(
        AppDbContext context, 
        ILogger<RedirectController> logger,
        IDistributedCache cache)
    {
        _context = context;

        // Injected into the constructor just like AppDbContext
        _logger = logger;

        _cache = cache;
    }

    [HttpGet("{shortCode}")]
    public async Task<IActionResult> RedirectToStoredURL(string shortCode)
    {
        string redirectUrl;

        // Declare Cache-Key to be used by Redis
        var cacheKey = $"shorturl:{shortCode}";

        // This variable will contain:
        // If that shortCode is cached => cachedUrl will stored the destination URL
        // If not, then it will storing "null"
        var cachedUrl = await _cache.GetStringAsync(cacheKey);

        // If "cachedUrl" is not Null or WhiteSpace => string.IsNullOrWhiteSpace(cachedUrl) = False => !string.... = True
        // cachedUrl can be:
        //      null
        //      ""
        //      " "
        //      real URL
        // Thus using "string." is to make sure that "cachedUrl" is real url => valid cache hit 
        if (!string.IsNullOrWhiteSpace(cachedUrl))
        {
            _logger.LogInformation(
                "Short URL cache hit. ShortCode: {ShortCode}", shortCode
            );

            redirectUrl = cachedUrl;
        }
        else
        {
            _logger.LogInformation(
                "Short URL cache miss. ShortCode: {ShortCode}", shortCode
            );

            var entity = await _context.ShortUrls.FirstOrDefaultAsync(s => s.ShortCode == shortCode);

            // Warning is being used here instead of Error, because:
            // Warning = unusual / suspicious, but the app still handled it.
            // While Error = the app failed to do something it was expected to do.
            // In this case, a missing short code won't stop / halt the running app:
            // 1. User typed it wrong.
            // 2. Old link.
            // 3. Bot Scanning random short codes
            // 4. Someone is probing this application's service
            if (entity is null) 
            {
                _logger.LogWarning(
                    "Redirect failed because the input short code was not found. ShortCode: {ShortCode}", shortCode
                );

                return NotFound();
            }

            // Check if the requested entity has expiresAt value is expired or not
            // If yes, then delete its cache and return StatusCode 410
            if (entity.ExpiresAt.HasValue && entity.ExpiresAt.Value <= DateTime.UtcNow)
            {
                _logger.LogInformation(
                    "Redirect failed because short URL expired. ShortCode: {ShortCode}", shortCode
                );

                await _cache.RemoveAsync(cacheKey);

                return StatusCode(StatusCodes.Status410Gone, "Short URL has expired.");
            }
        
            // A Logic Check to make sure if the retrieved URL from database is not broken (null or whitespace)
            // This should be used with Error for Logging, because:
            // If the row exists but "Url" is empty, this means that this application system has bad stored data.
            // The user did not make a bad request, but the server found an invalid internal state
            if (string.IsNullOrWhiteSpace(entity.Url))
            {
                _logger.LogError(
                    "Redirect failed because stored Url was empty. ShortCode: {ShortCode}", shortCode
                );
                return StatusCode(StatusCodes.Status500InternalServerError);
            }

            redirectUrl = entity.Url;

            // If the request entity has no expiration date: Redis will cache for normal 10 minutes.
            // If the time until the Expiration of the request entity (entity.ExpiresAt.Value - Datetime.UtcNow), is more than 10 minutes => cache for 10 minutes normally.
            // Else, Redis should cache for the remaining time before expiration.
            var cacheLifeTime = TimeSpan.FromMinutes(10);

            if (entity.ExpiresAt.HasValue)
            {
                var timeUntilExpiration = entity.ExpiresAt.Value - DateTime.UtcNow;

                if (timeUntilExpiration < cacheLifeTime)
                {
                    cacheLifeTime = timeUntilExpiration;
                }
            }

            await _cache.SetStringAsync(
                cacheKey,
                redirectUrl,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = cacheLifeTime
                }
            );
            
            _logger.LogInformation(
                "Short URL cached. ShortCode: {ShortCode}", shortCode
            );
        }

        // Old Approach
        // entity.AccessCount += 1;
        // entity.LastAccessedAt = DateTime.UtcNow;
        // await _context.SaveChangesAsync();

        // We already loaded the entity above so we can read/validate entity.Url for the redirect.
        // Do not update AccessCount by doing:
        //     entity.AccessCount += 1;
        //     await _context.SaveChangesAsync();
        // That read-modify-write pattern can lose clicks when two requests happen at the same time:
        // both requests may read the same AccessCount value, increment it in C#, and save the same result.
        // ExecuteUpdateAsync sends the increment to PostgreSQL instead:
        //     AccessCount = AccessCount + 1
        // PostgreSQL performs that update atomically, so concurrent redirects are less likely to overwrite each other's analytics updates.
        await _context.ShortUrls
            .Where(s => s.ShortCode == shortCode)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.AccessCount, s => s.AccessCount + 1)
                .SetProperty(s => s.LastAccessedAt, s => DateTime.UtcNow));

        _logger.LogInformation(
            "Short URL redirect succeeded. ShortCode: {ShortCode}", shortCode
        );
        
        return Redirect(redirectUrl);
    }
}

