// 1st - HTTP request arrives
// 2nd - ASP.NET needs to create ShortenController
// 3rd - ShortenController's Constructor requires AppDbContext
// 4th - ASP.NET creates AppDbContext using registered options
// 5th - ASP.NET passes it into the controller
// By default, AppDbContext registeres it with a scoped lifetime => 1 AppDbContext instance per HTTP request
// Workflow
// 1. ASP.NET receives the request.
// 2. ASP.NET creates ShortenController.
// 3. DI creates one AppDbContext for the request.
// 4. The controller creates a ShortUrlStorage object.
// 5. ShortUrls.Add(entity) marks it as Added.
// 6. SaveChangesAsync() asks EF to persist tracked changes.
// 7. EF uses the model configuration.
// 8. Npgsql converts the operation into PostgreSQL communication.
// 9. PostgreSQL checks constraints, including unique ShortCode.
// 10. PostgreSQL inserts the row.
// 11. EF updates entity.Id with the generated database ID.
// 12. The request finishes and the context is disposed.
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
            await SaveShortUrlWithRetryAsync(entity);

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
            LastAccessedAt = entity.LastAccessedAt,
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
            // Table "ShortUrls" là một attribute của _context
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
                // After "SaveChangesAsync() fails due to Unique-ShortCode-Violation, Entity Framework still tracks this failed entity as a pending insertion
                // Thus, we need to detach it from the current state of EF - which is assign for tracking this failed attempt.
                // This will allow assigning a new code and the entity as a fresh attempt.
                // Otherwise, EF's change tracker may retain state from failed save
                // Nó tương tự như memory slot trong chơi game nhưng ko có khả năng overwrite nên ta cần delete nó để có thể lưu cái info mới vô nó
                // By default, any code that cause change to the entity => EF tracks entity changes (state)
                _context.Entry(entity).State = EntityState.Detached;
                entity.ShortCode = GenerateUniqueShortCode();
            }
        }

        throw new InvalidOperationException("Unable to generate a unique short code after multiple attempts.");
    }

    // Providing a Yes / No answer to the question: "Did SaveChangeAsync() failed because PostgreSQL rejected a duplicate value?"
    // "Private" means the codes inside "ShortenController" can call this method but nowhere else
    // "static" means this method does not use controller instance data, _context. It only examines the exception passed to it
    // "DbUpdateException exception" - input parameter, EF Core will throws this if it cannot save a database change.
    // "InnerException": EF Core sits between this application and Docker PostgreSQL => the exception can therefore have layers:
    // - EF Core's exception = "DbUpdateException"
    private static bool IsUniqueShortCodeViolation(DbUpdateException exception)
    {
        // exception.InnerException is the way to access the underlying error
        // "exception.InnerException is PostgresException postgresException" - checks whether "InnerException" is a "PostgresException"
        // If yes, stores it in a new variable called postgresException
        // "postgresException.SqlState == PostgresErrorCodes.UniqueViolation" - PostgreSQL assigns standardized codes to errors and unique-contraint violation uses SQLSTATE == 23505 which can be written as ".UniqueViolation"
        // The unique-constraint is a database rule states that "No 2 rows may have the same value in this column, or combination of columns"
        // "ConstraintName" is an attribute can be use to identify the name of the database contraint
        // Vì nếu trong tương lai, making Url unique thì nếu thiếu "ConstraintName" sẽ ko làm ta biết rõ nguyên nhân nào gây ra UniqueViolation vì duplicated shortCode và duplicated URL đều cho ra UniqueViolation
        // "UniqueViolation" = identifies the category of error
        // "ConstraintName" = identifies the specific database rule that failed
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

