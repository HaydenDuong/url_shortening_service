using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using url_shortening_service.Data;
using url_shortening_service.Models;

namespace url_shortening_service.Controllers;

// [ApiController] = API behaviors (e.g automatic return 400 for bad model)
[ApiController]

// All actions in this class start with /shorten
// Url prefix based on coder design
[Route("shorten")]
public class ShortenController : ControllerBase
{
    // Field holds the DB session for this request (Injected by Dependencies Injection)
    private readonly AppDbContext _context;

    // Constructor
    // ASP.NET sees "needs AppDbContext" -> Creates AppDbContext(options)
    // NOT called once at startup.
    // Called once PER HTTP request when ASP.NET creates this controller.
    // Each time, DI passes a NEW AppDbContext that connects to the same "UrlShortenerDB" store.
    public ShortenController(AppDbContext context)
    {
        _context = context;
    }

    // Create a shorten URL for input URL via POST method
    // Body JSON: { "url" : "https://..."}
    // [HttpPost] = built-in attribute
    // async Task<ActionResult<ShortUrlResponse>> = Framework types - async action that returns HTTP result + JSON type
    // ActionResult<ShortUrlResponse> = Framework - Wrapper for 201 + body, 400, etc.
    // Create() - "Create" is based on one's choice
    // [FromBody] is needed because url is input in JSON body like {"url": "..."} - see url_shortening_service.http file
    // async - means this function can use "await" inside it
    // await - pause this method until the operation with "await" is done
    [HttpPost]
    public async Task<ActionResult<ShortUrlResponse>> Create(
        [FromBody] ShortUrlCreateUpdate request)
        {
            // Check if client sends a no JSON body request or malformed body that cannot be bound properly
            if (request is null)
            {
                return BadRequest("Request body is required.");
            }

            // Check if the input URL value is exist, is not blank, is real absolute URL, and using http / https
            // Try to normalize the URL if valid
            // TryNormalizeHttpUrl will return TRUE if the URL is valid, else it will be FALSE
            // "out var normalizedUrl" means the method can return a YES / No result and also output a cleaned-up URL value
            // If the URL is valid, "normalizedUrl" gets assigned a safe value like: "https://www.google.com/"
            // "out" is used in this case because we want to make sure this URL is valid, and save it as cleaned version
            // Because, request.Url could be:
            // empty, whitespace, "hello", "www.google.com" (missing http// or https// before it), "ftp://example.com"
            // normalizedUrl ở đây sẽ là output do có included "out" trước var normalizedUrl
            // Actuall logic behind "TryNormalizeHttpUrl() is":
            // private static bool TryNormalizeHttpUrl(string? rawUrl, out string normalizedUrl)
            // {
            //      normalizedUrl = string.Empty;

            //      if (string.IsNullOrWhiteSpace(rawUrl))
            //      {
            //          return false;
            //      }

            //      if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
            //      {
            //            return false;
            //      }

            //      if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            //      {
            //            return false;
            //      }

            //      normalizedUrl = uri.ToString();
            //      return true;
            // }
            if (!TryNormalizeHttpUrl(request.Url, out var normalizedUrl))
            {
                return BadRequest("Url must be a valid absolute http or https URL.");
            }

            // --- 1) Generate a random short code (random + unique) ---
            string shortCode = GenerateUniqueShortCode();

            // --- 2) Build Entity (DB row) - server sets everything except Url from client ---
            var now = DateTime.UtcNow;
            var entity = new ShortUrlStorage
            {
                Url = normalizedUrl,
                ShortCode = shortCode,
                CreatedAt = now,
                UpdatedAt = now,
                AccessCount = 0
            };

            // --- 3) Stage row in Entity Framework (not committed until SaveChanges) ---
            _context.ShortUrls.Add(entity);
            await _context.SaveChangesAsync();

            // --- 4) Map to response DTO (no AccessCount on normal response) ---
            var response = new ShortUrlResponse
            {
                Id = entity.Id,
                Url = entity.Url,
                ShortCode = entity.ShortCode,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt
            };

            // --- 5) 201 Created + body (Location header points at future GET URL) ---
            return Created($"/shorten/{entity.ShortCode}", response);
        }
    
    // Retrieving a shortCode
    // GET http://localhost:5184/shorten/abc123
    [HttpGet("{shortCode}")]
    public async Task<ActionResult<ShortUrlResponse>> GetByShortCode(string shortCode)
    {
        // shortCode comes from URL - "abc123", not from JSON body
        var entity = await _context.ShortUrls.FirstOrDefaultAsync(s => s.ShortCode == shortCode);

        if (entity is null) return NotFound(); // 404 - Not Found

        var response = new ShortUrlResponse
        {
            Id = entity.Id,
            Url = entity.Url,
            ShortCode = entity.ShortCode,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };

        return Ok(response); // 200 + JSON
    }

    // Update the existing URL in DB
    [HttpPut("{shortCode}")]
    public async Task<ActionResult<ShortUrlResponse>> UpdateUrl(
        string shortCode,
        [FromBody] ShortUrlCreateUpdate request)
    {
        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        if (!TryNormalizeHttpUrl(request.Url, out var normalizedUrl))
        {
            return BadRequest("Url must be a valid absolute http or https URL.");
        }

        // check if the shortCode from URL is legit or not
        var entity = await _context.ShortUrls.FirstOrDefaultAsync(s => s.ShortCode == shortCode);

        if (entity is null) return NotFound();

        entity.Url = normalizedUrl;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Create a new Response to return back to UI
        var response = new ShortUrlResponse
        {
            Id = entity.Id,
            Url = entity.Url,
            ShortCode = entity.ShortCode,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };

        return Ok(response);

    }

    // Delete an existing record based on shortCode in input URL
    [HttpDelete("{shortCode}")]
    public async Task<ActionResult> Delete(string shortCode)
    {
        var entity = await _context.ShortUrls.FirstOrDefaultAsync(s => s.ShortCode == shortCode);

        if (entity is null) return NotFound();

        _context.ShortUrls.Remove(entity);
        await _context.SaveChangesAsync();

        return NoContent(); //204 - No Content, empty body
    }

    // GET Statistic for a particular shortCode based on input URL
    [HttpGet("{shortCode}/stats")]
    public async Task<ActionResult<ShortUrlResponseStats>> GetStat(string shortCode)
    {
        var entity = await _context.ShortUrls.FirstOrDefaultAsync(s => s.ShortCode == shortCode);

        if (entity is null) return NotFound();

        var response = new ShortUrlResponseStats
        {
            Id = entity.Id,
            Url = entity.Url,
            ShortCode = entity.ShortCode,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            AccessCount = entity.AccessCount
        };

        return Ok(response);
    }

    // Method for Generate Unique ShortCode for input URL
    private string GenerateUniqueShortCode()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        while(true)
        {
            // Generate a code of 6 random chars
            var code = new string(Enumerable.Range(0, 6)
                .Select(_ => chars[Random.Shared.Next(chars.Length)])
                .ToArray());
            
            // Check if this code is present or not
            bool exists = _context.ShortUrls.Any(s => s.ShortCode == code);
            if (!exists)
                return code;
        }
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

