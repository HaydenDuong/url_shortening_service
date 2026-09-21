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
        modelBuilder.Entity<ShortUrlStorage>()
            .HasIndex(e => e.ShortCode)
            .IsUnique();
    }
}

