using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using url_shortening_service.Data;

namespace url_shortening_service.Controllers;

[ApiController]
[Route("go")]
public class RedirectController : ControllerBase
{
    private readonly AppDbContext _context;
    // Added a logger field to inject into the constructor
    private readonly ILogger<RedirectController> _logger;

    public RedirectController(AppDbContext context, ILogger<RedirectController> logger)
    {
        _context = context;

        // Injected into the constructor just like AppDbContext
        _logger = logger;
    }

    [HttpGet("{shortCode}")]
    public async Task<IActionResult> RedirectToStoredURL(string shortCode)
    {
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
        
        return Redirect(entity.Url);
    }
}

