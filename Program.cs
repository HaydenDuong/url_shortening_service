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
// "builder.Configuration.GetConnectionString("Postgres")" = Asks ASP.NET Core to give the final value of ConnectionString:Postgres after all config sources are combined
// Locally, the final value comes from: "appsettings.json"
// In Docker Compose, the final value comes from: "Docker/docker-compose.yml - ConnectionStrings__Postgres"
// As result, this latter (environment variables) override JSON config
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// When running inside Docker, skip HTTPS redirection for now
// Else, keep HTTPS redirection when running normally on local machine instead of Docker
// .NET containers automatically set "DOTNET_RUNNING_IN_CONTAINER=true" 
// In production, HTTPS us often handled by something in front of the app:
//      Reverse Proxy
//      Load Balancer
//      API Gateway
//      Nginx
//      Cloud Service
// The app container often receives plains HTTP internally, while HTTPS is handled at the edge
if (!builder.Configuration.GetValue<bool>("DOTNET_RUNNING_IN_CONTAINER"))
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
