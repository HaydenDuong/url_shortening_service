// =============================================================================
// AppDbContext.cs — "Bridge" between our C# app and the database
// =============================================================================
// Entity Framework (EF) uses this class to:
//   - Know which tables we have (DbSet)
//   - Add / find / delete rows (ShortUrlStorage)
//   - Save changes when we call SaveChangesAsync()
//
// We do NOT create this with "new AppDbContext()" in controllers.
// Program.cs registers it; ASP.NET DI creates it per HTTP request.
// Hãy xem AppDBContext giống task list chỉ chứa 1 task duy nhất và nó dùng để truyền cho constructor (worker) để làm việc với ShortUrlDB
// Do vậy, AppDbContext và constructor sẽ được gọi cho mỗi một HTTP request và mỗi lần đều là mới để ko bị trùng data hay lỗi sai 
// AppDbContext is EF Core's working session with the database
// Nên nhớ, controllers does not manually write SQL. "AppDbContext" & "EF Core" translate those C# operations into PostgreSQL commands.
// =============================================================================
using Microsoft.EntityFrameworkCore;
using url_shortening_service.Models;

// "Data" folder namespace — keeps database code separate from Models/Controllers
namespace url_shortening_service.Data;

// class AppDbContext (Custom - this app database session) is a child and inherited properties from DbContext (built-in framework / engine)
// "DbContext" is the central class in EF Core that represents a session with database - acts as a bridge between current application's entity classes and the underlying database
// "DbContext" is EF Core's built-in base class that provides the following functionalities:
// - Querying tables
// - Tracking entity changes
// - Generating SQL
// - Opening database connections when needed
// - Saving changes
// - Mapping C# classes to database tables
// => "AppDbContext is a specialized DbContext"
public class AppDbContext : DbContext
{

    // -------------------------------------------------------------------------
    // CONSTRUCTOR — DI (Dependency Injection) passes in "how to connect"
    // -------------------------------------------------------------------------
    // When a controller asks for AppDbContext, ASP.NET calls this constructor.
    //
    // DbContextOptions<AppDbContext> options = this object contains configuration describing how this context should operate
    // (In this case: builder.Services.AddDbContext<AppDbContext>( options => options.UseNpgsql(connectionString)); - In Program.cs)
    // Conceptually, the options contain information like:
    // Database Provider: PostgreSQL
    // Connection String: Host=localhost; Port=5434;... (in appsettings.json)
    // Context type: AppDbContext
    // => The constructor receives those prepared settings through dependency injection
    // Empty { } body is normal — we are not doing extra setup here.
    // Since "DbContext" is the parent class and AppDbContext needs options because the parent class contains the machinery that communicates with databases
    // Thus, ": base(option)" , where "base" is from DbContext
    // => "Before constructing AppDbContext, call the DbContext constructor and give this latter these options"
    // ASP.NET prepares options (in Program.cs)
    //                   ||
    // ASP.NET calles new AppDbContext(options)
    //                   ||
    // AppDbContext passes options to DbContext
    //                   ||
    // DbContext knows it should use PostgreSQL
    // -------------------------------------------------------------------------
    public AppDbContext(DbContextOptions<AppDbContext> options): base(options)
    {
        
    }

    // Hãy nghĩ là ta cần define cái table nào sẽ được dùng và dạng model nào sẽ được lưu vô table đó
    // Phải báo thì AppDbContext mới biết cái nào để làm
    // Bên Program.cs có chứa information về database và information để kết nối với PostgreSQL và cái Database nào:
    // ("ConnectionStrings": {"Postgres": "Host=localhost;Port=5434;Database=UrlShortenerDB;Username=postgres;Password=postgres"})
    // Dòng code này dùng để báo với EF Core là: this application model contains a collection of "ShortUrlStorage" entities
    // DbSet<ShortUrlStorage> = the table / query starting point
    // "ShortUrlStorage" = one database row
    // "ShortUrls" is the name given for the table that will be initialized (once) based on the information written in "Models/ShortUrlStorage.cs"
    // This table is belong to a database (one database can have a collection of different tables at once)
    // -------------------------------------------------------------------------
    // DbSet = "all rows in one table"
    // -------------------------------------------------------------------------
    // ShortUrls is the name WE chose
    // ShortUrlStorage is the C# shape of ONE row (see Models/ShortUrlStorage.cs)
    //
    // Examples we'll use later:
    //   _context.ShortUrls.Add(newRow);
    //   _context.ShortUrls.FirstOrDefaultAsync(x => x.ShortCode == "abc123");
    //
    // Until SaveChangesAsync(), Add only stages the row in memory (like a draft).
    // -------------------------------------------------------------------------
    public DbSet<ShortUrlStorage> ShortUrls { get; set; }

    // -------------------------------------------------------------------------
    // OnModelCreating — rules about tables/columns (not everyday queries)
    // -------------------------------------------------------------------------
    // EF calls this once when building the database model.
    // Use it for: unique indexes, required fields, max lengths, relationships.
    //
    // Here: ShortCode must be UNIQUE (roadmap requirement — no two links same code)
    // "Protected" = the method is intended for this class and inherited classes, not controllers or unrelated code
    // Since "DbContext" has defined an "OnModelCreating" method so => "Override" is needed to make custom one
    // "ModelBuilder" - EF Core's configuration tool that use for describe:
    // - indexes
    // - uniqueness
    // - required columns
    // - maximum lengths
    // - relationship
    // - table names
    // - column behavior
    // When Migration happened:
    // 1st - C# model configuration changes
    // 2nd - Type "dotnet ef migration add ..."
    // 3rd - EF creates migration instruction
    // 4th - Type "dotnet ef database update"
    // 5th - PostgreSQL schema changes
    // -------------------------------------------------------------------------
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure the database mapping for "ShortUrlStorage"
        // ".HasIndex" = create an index based on the ShortCode property
        // ".IsUnique()" = Do not allow two rows to have the same indexed value
        modelBuilder.Entity<ShortUrlStorage>()
            .HasIndex(e => e.ShortCode)
            .IsUnique();
    }
}

