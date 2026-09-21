using Microsoft.EntityFrameworkCore;
using url_shortening_service.Models;
namespace url_shortening_service.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options): base(options)
    {
        
    }
    public DbSet<ShortUrlStorage> ShortUrls { get; set; }

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

