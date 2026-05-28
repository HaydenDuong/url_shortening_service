//   Program.cs                    Request (POST /shorten)
//   AddDbContext ───────────────► DI creates AppDbContext
//        │                              │
//        │ options: InMemory            ▼
//        └────────────────────► Controller uses _context.ShortUrls
//                                        │
//                                        ▼
//                                 SaveChangesAsync() → row stored

using Microsoft.EntityFrameworkCore;
using url_shortening_service.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Tell DI: "When someone needs AppDbContext, build it like this."

// UseInMemoryDatabase = fake DB in RAM (data gone when app stops — fine for learning).
// Later swap to UseNpgsql(connectionString) for real Postgres — AppDbContext file stays same.
// "UrlShortenerDB" is a database name
// builder.Services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase("UrlShortenerDB"));

// Using PostgreSQL instead of RAM
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
