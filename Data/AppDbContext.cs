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
// =============================================================================
// Hãy xem AppDBContext giống task list chỉ chứa 1 task duy nhất và nó dùng để truyền cho constructor (worker) để làm việc với ShortUrlDB
// Do vậy, AppDbContext và constructor sẽ được gọi cho mỗi một HTTP request và mỗi lần đều là mới để ko bị trùng data hay lỗi sai 
using Microsoft.EntityFrameworkCore;
using url_shortening_service.Models;

// "Data" folder namespace — keeps database code separate from Models/Controllers
namespace url_shortening_service.Data;

// class AppDbContext (Custom - this app database session) is a child and inherited properties from DbContext (built-in framework / engine)
// : DbContext means "this IS an EF database session" — inherit EF's built-in powers
public class AppDbContext : DbContext
{

    // -------------------------------------------------------------------------
    // CONSTRUCTOR — DI (Dependency Injection) passes in "how to connect"
    // -------------------------------------------------------------------------
    // When a controller asks for AppDbContext, ASP.NET calls this constructor.
    //
    // DbContextOptions<AppDbContext> = a lunchbox of settings filled in Program.cs
    //   (right now: UseInMemoryDatabase — later: UseNpgsql for Postgres)
    //
    // : base(options) = hand that lunchbox to the parent DbContext class
    //   (the parent actually knows how to talk to the database)
    //
    // Empty { } body is normal — we are not doing extra setup here.
    // -------------------------------------------------------------------------
    // ONE "options" object from Program.cs (how to connect to DB).
    // We do not use "options" in { } because the PARENT DbContext needs it.
    // : base(options) means: "Parent constructor, take this object first."
    //   (NOT inheritance — that was already said on "class AppDbContext : DbContext")
    // base = parent Class (DbContext)
    // base(options) = call parent's constructor and pass "options"
    public AppDbContext(DbContextOptions<AppDbContext> options): base(options)
    {
        
    }

    // DbSet<ShortUrlStorage> is the data model that being used for this app
    // "ShortUrls" is the name given for the table that will be initialized (once) based on the information written in "Models/ShortUrlStorage.cs"
    // This table is belong to a database (one database can have a collection of different tables at once)
    // -------------------------------------------------------------------------
    // DbSet = "all rows in one table"
    // -------------------------------------------------------------------------
    // ShortUrls is the name WE chose (could be Links, Urls, etc.)
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
    // -------------------------------------------------------------------------
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShortUrlStorage>()
            .HasIndex(e => e.ShortCode)
            .IsUnique();
    }
}

