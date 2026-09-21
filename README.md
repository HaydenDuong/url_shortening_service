# URL Shortening Service

Backend-focused URL shortening service built with ASP.NET Core, PostgreSQL, Redis, RabbitMQ and Docker Compose.

The project explores backend concerns beyond basic CRUD, including cache consistency, concurrent analytics updates, asynchronous messaging, rate limiting and URL expiration.

## Key Features

- Create, update, retrieve and delete short URLs
- Redirect using generated short codes
- PostgreSQL persistence with EF Core
- Unique short-code constraint with collision retry
- Redis read-through caching
- Cache invalidation on update and deletion
- RabbitMQ-based asynchronous click analytics
- Atomic database updates for click counters
- URL expiration and cleanup
- ASP.NET Core rate limiting
- Structured logging
- Docker Compose local environment

## Architecture

Client
  ↓
ASP.NET Core API
  ├── PostgreSQL — source of truth
  ├── Redis — redirect cache
  └── RabbitMQ — asynchronous analytics
                      ↓
                Background Consumer
                      ↓
                  PostgreSQL

### Known Limitation

The current analytics publisher opens a RabbitMQ connection per publish operation.
A production implementation would reuse a long-lived connection and manage channel lifecycle separately.

## Running Locally

1. Copy `.env.example` to `.env`
2. Update local credentials if needed
3. Start the stack:

```bash
docker compose -f Docker/docker-compose.yml up --build