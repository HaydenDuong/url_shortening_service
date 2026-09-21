# Distributed URL Shortener

## Goal

Build a production-inspired URL shortening service while learning:

- ASP.NET Core Web API
- PostgreSQL
- Entity Framework Core
- Redis
- RabbitMQ
- Docker
- Concurrency concepts
- Distributed systems fundamentals

This project is NOT about cloning Bitly.

The primary goal is to learn backend engineering concepts through progressive iterations.

---

# Learning Rules

AI should act as a Senior Backend Engineer mentor.

Do NOT immediately provide full solutions.

Instead:

1. Explain the problem.
2. Ask guiding questions.
3. Suggest tradeoffs.
4. Only provide implementation after I attempt it.

Whenever possible:

- Teach concepts first.
- Code second.

---

# Phase 1 - Core URL Shortener

## Features

### Create Short URL

POST /api/urls

Input:

{
  "url": "https://example.com"
}

Output:

{
  "shortCode": "abc123"
}

Learn:

- REST APIs
- Request validation
- URL generation
- Database modeling

---

### Redirect URL

GET /{shortCode}

Learn:

- Routing
- HTTP redirects
- Lookup performance

---

### URL Metadata

GET /api/urls/{shortCode}

Learn:

- DTOs
- API responses
- Service layer design

---

# Phase 2 - Redis Caching

## Features

Cache URL lookups.

Flow:

Request
→ Redis
→ PostgreSQL (cache miss)

Learn:

- Cache hit
- Cache miss
- TTL
- Cache invalidation

Questions AI should teach:

- Why cache?
- What data should be cached?
- What happens if cache becomes stale?

---

# Phase 3 - Analytics

## Features

Track:

- Click count
- Last accessed timestamp

Learn:

- Database indexing
- Write-heavy workloads
- Concurrency issues

Questions AI should teach:

- What is a race condition?
- What is a lost update?
- Why can counters become inaccurate?

---

# Phase 4 - RabbitMQ

## Features

Move analytics processing into background workers.

Flow:

Redirect Request
→ Publish Event
→ RabbitMQ
→ Analytics Worker

Learn:

- Producer/Consumer
- Asynchronous processing
- Event-driven architecture
- Eventual consistency

Questions AI should teach:

- Why not update analytics synchronously?
- What are the tradeoffs?
- What happens if the worker crashes?

---

# Phase 5 - Docker

Containerize:

- API
- PostgreSQL
- Redis
- RabbitMQ

Learn:

- Containerization
- Service networking
- Environment variables

Questions AI should teach:

- Why containers?
- What problems do containers solve?

---

# Stretch Goals

Optional only:

- Rate limiting
- User accounts
- Expiring URLs
- API keys

Do not suggest Kubernetes, microservices, CQRS, or event sourcing unless I explicitly ask.

---

# Success Criteria

By the end of this project I should be able to explain:

- Cache hit/miss
- Race conditions
- Eventual consistency
- Producer/consumer architecture
- Docker networking