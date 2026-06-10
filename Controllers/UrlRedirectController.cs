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

        entity.AccessCount += 1;
        await _context.SaveChangesAsync();

        return Redirect(entity.Url);
    }
}

