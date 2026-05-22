using System.Data;
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
    [HttpPost]
    public async Task<ActionResult<ShortUrlResponse>> Create(
        [FromBody] ShortUrlCreateUpdate request)
        {
            // --- 1) Generate a random short code (random + unique) ---
            string shortCode = GenerateUniqueShortCode();

            // --- 2) Build Entity (DB row) - server sets everything except Url from client ---
            var now = DateTime.UtcNow;
            var entity = new ShortUrlStorage
            {
                Url = request.Url,
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
}

