using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using url_shortening_service.Data;
using url_shortening_service.Models;
using Microsoft.Extensions.Caching.Distributed;

namespace url_shortening_service.Controllers;
[ApiController]

[Route("shorten")]
public class ShortenController : ControllerBase
{
    private readonly AppDbContext _context;

    private readonly ILogger<ShortenController> _logger;

    private readonly IDistributedCache _cache;

    public ShortenController(AppDbContext context, ILogger<ShortenController> logger, IDistributedCache cache)
    {
        _context = context;
        _logger = logger;
        _cache = cache;
    }

    [HttpPost]
    public async Task<ActionResult<ShortUrlResponse>> Create(
        [FromBody] ShortUrlCreateUpdate request)
    {
        if (request is null)
        {
            _logger.LogWarning(
                "Create short URL failed because the request body was missing"
            );

            return BadRequest("Request body is required.");
        }

        if (!TryNormalizeHttpUrl(request.Url, out var normalizedUrl))
        {
            _logger.LogWarning(
                "Create short URL failed because input URL was invalid."
            );

            return BadRequest("Url must be a valid absolute http or https URL.");
        }

        if (request.ExpiresAt != null && request.ExpiresAt <= DateTime.UtcNow)
        {   
            _logger.LogWarning(
                "Create short URL failed because ExpiresAt was in the past."
            );

            return BadRequest("The provided Expires Date is not valid");
        }

        string shortCode = GenerateUniqueShortCode();

        var now = DateTime.UtcNow;
        var entity = new ShortUrlStorage
        {
            Url = normalizedUrl,
            ShortCode = shortCode,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = request.ExpiresAt,
            AccessCount = 0
        };

        // The database unique constraint is the final guard against concurrent
        // requests generating the same short code.
        await SaveShortUrlWithRetryAsync(entity);
            
        _logger.LogInformation(
            "Short URL created. ShortCode: {ShortCode}", entity.ShortCode
        );

        var response = new ShortUrlResponse
        {
            Id = entity.Id,
            Url = entity.Url,
            ShortCode = entity.ShortCode,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            ExpiresAt = entity.ExpiresAt
        };

        return Created($"/shorten/{entity.ShortCode}", response);
    }
    
    [HttpGet("{shortCode}")]
    public async Task<ActionResult<ShortUrlResponse>> GetByShortCode(string shortCode)
    {
        var entity = await _context.ShortUrls.FirstOrDefaultAsync(s => s.ShortCode == shortCode);

        if (entity is null)
        {
            _logger.LogWarning(
                "Get short URL failed because short code was not found. ShortCode: {ShortCode}", shortCode
            );

            return NotFound();
        } 

        var response = new ShortUrlResponse
        {
            Id = entity.Id,
            Url = entity.Url,
            ShortCode = entity.ShortCode,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            ExpiresAt = entity.ExpiresAt
        };

        return Ok(response);
    }

    [HttpPut("{shortCode}")]
    public async Task<ActionResult<ShortUrlResponse>> UpdateUrl(
        string shortCode,
        [FromBody] ShortUrlCreateUpdate request)
    {
        if (request is null)
        {
            _logger.LogWarning(
                "Update short URL failed because the request body was missing. ShortCode: {ShortCode}", shortCode
            );

            return BadRequest("Request body is required.");
        }

        if (!TryNormalizeHttpUrl(request.Url, out var normalizedUrl))
        {
            _logger.LogWarning(
                "Update short URL failed because input URL was invalid. ShortCode: {ShortCode}", shortCode
            );

            return BadRequest("Url must be a valid absolute http or https URL.");
        }

        if (request.ExpiresAt != null && request.ExpiresAt <= DateTime.UtcNow)
        {
            _logger.LogWarning(
                "Update short URL failed because ExpiresAt was in the past. ShortCode: {ShortCode}", shortCode
            );

            return BadRequest("The Expires Date must be a date in the future.");
        }

        var entity = await _context.ShortUrls.FirstOrDefaultAsync(s => s.ShortCode == shortCode);

        if (entity is null) 
        {
            _logger.LogWarning(
                "Update short URL failed because short code was not found. ShortCode: {ShortCode}", shortCode
            );

            return NotFound();
        }

        entity.Url = normalizedUrl;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.ExpiresAt = request.ExpiresAt;

        await _context.SaveChangesAsync();

        var cacheKey = $"shorturl:{shortCode}";
        await _cache.RemoveAsync(cacheKey);

        _logger.LogInformation(
            "Short URL cache invalidated after update. ShortCode: {ShortCode}", entity.ShortCode
        );

        _logger.LogInformation(
            "Short URL updated. ShortCode: {ShortCode}", entity.ShortCode
        );

        var response = new ShortUrlResponse
        {
            Id = entity.Id,
            Url = entity.Url,
            ShortCode = entity.ShortCode,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            ExpiresAt = entity.ExpiresAt
        };

        return Ok(response);

    }

    [HttpDelete("cleanup/expired")]
    public async Task<ActionResult<int>> DeleteExpired()
    {
        var deletedCount = await _context.ShortUrls
            .Where(s => s.ExpiresAt.HasValue && s.ExpiresAt <= DateTime.UtcNow)
            .ExecuteDeleteAsync();
        
        _logger.LogInformation(
            "Expired short URLs deleted. Count: {DeletedCount}", deletedCount
        );

        return Ok(deletedCount);
    }

    [HttpDelete("{shortCode}")]
    public async Task<ActionResult> Delete(string shortCode)
    {
        var entity = await _context.ShortUrls.FirstOrDefaultAsync(s => s.ShortCode == shortCode);

        if (entity is null) 
        {
            _logger.LogWarning(
                "Delete short URL failed because short code was not found. ShortCode: {ShortCode}", shortCode
            );

            return NotFound();
        }

        _context.ShortUrls.Remove(entity);
        await _context.SaveChangesAsync();

        var cacheKey = $"shorturl:{shortCode}";
        await _cache.RemoveAsync(cacheKey);

        _logger.LogInformation(
            "Short URL cache invalidated after delete. ShortCode: {ShortCode}", shortCode
        );

        _logger.LogInformation(
            "Short URL deleted. ShortCode: {ShortCode}", shortCode
        );

        return NoContent();
    }

    [HttpGet("{shortCode}/stats")]
    public async Task<ActionResult<ShortUrlResponseStats>> GetStat(string shortCode)
    {
        var entity = await _context.ShortUrls.FirstOrDefaultAsync(s => s.ShortCode == shortCode);

        if (entity is null) 
        {
            _logger.LogWarning(
                "Get short URL stats failed because short code was not found. ShortCode: {ShortCode}", shortCode
            );

            return NotFound();
        }

        var response = new ShortUrlResponseStats
        {
            Id = entity.Id,
            Url = entity.Url,
            ShortCode = entity.ShortCode,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            ExpiresAt = entity.ExpiresAt,
            LastAccessedAt = entity.LastAccessedAt,
            AccessCount = entity.AccessCount
        };

        return Ok(response);
    }

    private string GenerateUniqueShortCode()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        while(true)
        {
            var code = new string(Enumerable.Range(0, 6)
                .Select(_ => chars[Random.Shared.Next(chars.Length)])
                .ToArray());
            
            bool exists = _context.ShortUrls.Any(s => s.ShortCode == code);
            if (!exists)
                return code;
        }
    }

    private async Task SaveShortUrlWithRetryAsync(ShortUrlStorage entity)
    {
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            _context.ShortUrls.Add(entity);

            try
            {
                await _context.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException ex) when (IsUniqueShortCodeViolation(ex) && attempt < maxAttempts)
            {
                _context.Entry(entity).State = EntityState.Detached;
                entity.ShortCode = GenerateUniqueShortCode();
            }
        }

        throw new InvalidOperationException("Unable to generate a unique short code after multiple attempts.");
    }

    private static bool IsUniqueShortCodeViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException 
        && postgresException.SqlState == PostgresErrorCodes.UniqueViolation 
        && postgresException.ConstraintName == "IX_ShortUrls_ShortCode";
    }

    private static bool TryNormalizeHttpUrl(string? rawUrl, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;

        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        normalizedUrl = uri.ToString();
        return true;
    }
}

