using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using url_shortening_service.Data;
using url_shortening_service.Services;
using Microsoft.Extensions.Caching.Distributed;

namespace url_shortening_service.Controllers;

[ApiController]
[Route("go")]
public class RedirectController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<RedirectController> _logger;
    private readonly IDistributedCache _cache;
    private readonly ShortUrlAnalyticsPublisher _analyticsPublisher;

    public RedirectController(
        AppDbContext context, 
        ILogger<RedirectController> logger,
        IDistributedCache cache,
        ShortUrlAnalyticsPublisher analyticsPublisher)
    {
        _context = context;

        _logger = logger;

        _cache = cache;

        _analyticsPublisher = analyticsPublisher;
    }

    [HttpGet("{shortCode}")]
    public async Task<IActionResult> RedirectToStoredURL(string shortCode)
    {
        string redirectUrl;

        var cacheKey = $"shorturl:{shortCode}";

        var cachedUrl = await _cache.GetStringAsync(cacheKey);

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

            if (entity is null) 
            {
                _logger.LogWarning(
                    "Redirect failed because the input short code was not found. ShortCode: {ShortCode}", shortCode
                );

                return NotFound();
            }

            if (entity.ExpiresAt.HasValue && entity.ExpiresAt.Value <= DateTime.UtcNow)
            {
                _logger.LogInformation(
                    "Redirect failed because short URL expired. ShortCode: {ShortCode}", shortCode
                );

                await _cache.RemoveAsync(cacheKey);

                return StatusCode(StatusCodes.Status410Gone, "Short URL has expired.");
            }
        
            if (string.IsNullOrWhiteSpace(entity.Url))
            {
                _logger.LogError(
                    "Redirect failed because stored Url was empty. ShortCode: {ShortCode}", shortCode
                );
                return StatusCode(StatusCodes.Status500InternalServerError);
            }

            redirectUrl = entity.Url;

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

        try
        {
            await _analyticsPublisher.PublishAccessedAsync(shortCode, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish short URL analytics event. ShortCode: {ShortCode}", shortCode
            );
        }

        _logger.LogInformation(
            "Short URL redirect succeeded. ShortCode: {ShortCode}", shortCode
        );
        
        return Redirect(redirectUrl);
    }
}

