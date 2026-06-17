using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using url_shortening_service.Data;

namespace url_shortening_service.Controllers;

[ApiController]
[Route("go")]
public class RedirectController : ControllerBase
{
    private readonly AppDbContext _context;

    public RedirectController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("{shortCode}")]
    public async Task<IActionResult> RedirectToStoredURL(string shortCode)
    {
        var entity = await _context.ShortUrls.FirstOrDefaultAsync(s => s.ShortCode == shortCode);

        if (entity is null) return NotFound();
        
        // A Logic Check to make sure if the retrieved URL from database is not broken (null or whitespace)
        if (string.IsNullOrWhiteSpace(entity.Url))
        {
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

        return Redirect(entity.Url);
    }
}

