# Dependency Injection Notes

## What Is Dependency Injection?

Dependency Injection means a class receives the tools it needs from ASP.NET Core instead of creating them manually.

Example from this project:

```csharp
public RedirectController(
    AppDbContext context,
    ILogger<RedirectController> logger,
    IDistributedCache cache)
```

The controller needs:

- `AppDbContext` to talk to PostgreSQL.
- `ILogger<RedirectController>` to write structured logs.
- `IDistributedCache` to use the Redis-backed cache.

These are called dependencies because the controller depends on them to do its job.

## Why Not Create Them Manually?

Avoid this style inside controllers:

```csharp
var context = new AppDbContext(...);
var cache = new RedisCache(...);
```

That would make the controller responsible for too many things:

- handling HTTP requests
- knowing how to connect to PostgreSQL
- knowing how to configure Redis
- knowing how to create loggers

Better separation:

- `Program.cs` configures services.
- ASP.NET Core creates/provides services.
- Controllers use services to handle requests.

## What Program.cs Does

`Program.cs` registers services with ASP.NET Core's dependency injection container.

Examples:

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"));
});

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
});
```

These lines mean:

- If something asks for `AppDbContext`, create it using the PostgreSQL connection string.
- If something asks for `IDistributedCache`, provide a Redis-backed cache.

`ILogger<T>` is already registered by ASP.NET Core.

## What Happens During A Request?

When a request arrives:

```text
GET /go/abc123
```

ASP.NET Core needs to create `RedirectController`.

It looks at the constructor:

```csharp
RedirectController(
    AppDbContext context,
    ILogger<RedirectController> logger,
    IDistributedCache cache)
```

Then ASP.NET Core provides those objects automatically.

## Why Inject IDistributedCache Instead Of Redis Directly?

`IDistributedCache` is an abstraction.

It represents the idea of a distributed cache:

- get a cached value
- set a cached value
- remove a cached value

The controller does not need to know every Redis detail.

The controller talks to:

```text
IDistributedCache
```

`Program.cs` decides that `IDistributedCache` is backed by Redis:

```csharp
builder.Services.AddStackExchangeRedisCache(...)
```

This keeps the controller focused on redirect logic instead of Redis setup.

## Simple Explanation

Dependency Injection means:

```text
Program.cs registers how to build tools.
The controller constructor asks for the tools it needs.
ASP.NET Core provides those tools when handling a request.
```

In this project:

```text
AppDbContext = database access
ILogger = structured logging
IDistributedCache = Redis caching
```

