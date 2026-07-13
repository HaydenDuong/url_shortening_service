# url_shortening_service

## Stage 1 - Setup ASP.NET Core Web API Project, Create HTTP Endpoints, Connect to PostgreSQL

### A. Setup ASP.NET Core Web API Project [Done]
- Create a new ASP.NET Core Web API Project via "dotnet new webapi -n "name_of_the_project"

### B. Create HTTP Endpoints [Done]

### C. Connect to PostgreSQL [Done]
- Install required NuGet packages:
    - dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
    - dotnet add package Microsoft.EntityFrameworkCore.Design

- Add "Connection String" to application's "appsettings.json" - This will tell the client how to reach the database of the server through providing host, port, database, user, and password:
    - {
        "ConnectionStrings": {
            "Postgres": "Host=localhost;Port=5432;Database=name_of_DB;Username=postgres(or any);Password=postgres(or any)"
        }
    }

- Update "Program.cs":
    - Replace "UseInMemoryDatabase(...)" with UseNpgsql(builder.Configuration.GetConnectionString("Postgres"))

- Create docker-compose.yml - this is for creating the database of the server with envir of user, password, and database (Server-side)
    - "docker compose up -d"

- Run Migration = instruction to create/update DB structure from EF Models (Should do regardless this Postgres Table is first of its kind in this app):
    - "dotnet tool install --global dotnet-ef"
    - "dotnet ef migrations add InitialPostgres"
    - "dotnet ef database update"
    - Check with: "docker exec -it container_name psql -U POSTGRES_USER_VALUE -d POSTGRES_DB_VALUE & SELECT * FROM "ShortUrls";
    - In this case: "docker exec -it urlshortener_webapp_DB psql -U postgres -d UrlShortenerDB" & 'SELECT * FROM "ShortUrls";'

- **Note** - dotnet restore / dotnet build will install the required dependencies


## Stage 2 - Redirect Logic, Validation, Collision Handling
- Add a seperate Controller.cs under /Controllers folder for Redirecting Logic.
- Add a line in .http to test Redirecting Logic.
- Adding validation logic to both: "UrlShorteningServiceController.cs" (HttpPOST & HttpPUT) & "ShortUrlCreateUpdate.cs" (Adding [Required] & [Url])
- Adding collision handling for race condition methods in "UrlShorteningServiceController.cs" (HttpPOST)

## Stage 3 - Analytics and Concurrency, Structured Logging, Dockerize the API (Analytics -> Logging -> Dockerfile -> Compose Networking -> Environment Config)
### A - Analytics and Concurrency
- Add basic analytics fields:
    - "AccessCount" = how many times the short URL has been used for redirect.
    - "LastAccessedAt" = the last time the short URL was successfully used.
- The redirect endpoint updates analytics when the user calls "GET /go/{shortCode}".
- "GET /shorten/{shortCode}/stats" only reads and returns the stored analytics values.

### Note - Lost Update and Atomic Database Update
- The first/simple approach was:
    - Load the row from PostgreSQL into C#.
    - Do "entity.AccessCount += 1".
    - Save the entity back to PostgreSQL.
- This can cause a lost update when two redirect requests happen at the same time.
    - Example: AccessCount is 10.
    - Request A reads 10.
    - Request B also reads 10.
    - Both requests add 1 and save 11.
    - The final value becomes 11, but the correct value should be 12.
- The improved approach uses "ExecuteUpdateAsync" to send the increment directly to PostgreSQL and take effect immediately at the point in which it is invoked. This helps prevent lost updates from happening:
    - "AccessCount = AccessCount + 1"
- This is safer because PostgreSQL updates the counter atomically inside the database, instead of C# reading an old value and writing back a possibly outdated result.

### B - Logging
- In "appsettings.json": 
    ```json
    "Logging": {
        "LogLevel": {
            "Default": "Information",
            "Microsoft.AspNetCore": "Warning"
        }
    }
    ```
- This means:
    - "Default" = minimum log level for most application logs. The "_logger" calls in both controller files fall under this category.
    - "Default": "Information" = app will show: Information, Warning, Error, and Critical. 
        - But not: Debug & Trace
    - "Microsoft.AspNetCore" = minimum log level for ASP.NET Core framework logs. This only shows Warning and above, which keeps the terminal from being flooded with framework noise.
    - The order of seriousness is as:
        - Trace
        - Debug
        - Information
        - Warning
        - Error
        - Critical

### Note - Structured Logging and Sensitive Data
- Prefer structured logging instead of string concatenation:
    - Good: "_logger.LogInformation(\"Short URL created. ShortCode: {ShortCode}\", shortCode);"
    - Avoid: "_logger.LogInformation(\"Short URL created: \" + shortCode);"
- Structured logging keeps the message readable and lets log systems search/filter by fields like "ShortCode".
- Avoid logging full destination URLs unless there is a very specific reason.
- Full URLs can contain sensitive data such as:
    - password reset tokens
    - email addresses
    - API keys
    - session IDs
    - private query string values
- Safer default: log the "ShortCode" and the event outcome, but not the full "Url".

### C - Dockerize the API (Build the ASP.NET Core API into a Docker image and run it as a container)
- Before this stage, the current app is running locally and the machine provides:
    - .NET SDK
    - NuGet packages
    - source code
    - dotnet run
- After this stage, a Docker image packages this app with the runtime it needs:
    - compiled app
    - ASP.NET runtime
    - startup command
    - container port

- Important Concept: SDK Image vs Runtime Image
    - Build Phase, has compiler and build tools:
        - Uses SDK image: "mcr.microsoft.com/dotnet/sdk"
        - This image can:
            - restore packages
            - build code
            - publish app
        - Quite big
    - Runtime Phase, has only enough .NET runtim to execute the compiled DLL:
        - Use ASP.NET runtime image: "mcr.microsoft.com/dotnet/aspnet"
        - This image can:
            - run the already-built app
        - Smaller and closer to production practice
    - Thus, this is known as multi-stage builds where:
        - Build with SDK.
        - Run with runtime only

- Steps:
    - 1. Create .dockerignore
    - 2. Create Dockerfile
    - 3. Build the API image
    - 4. Run the API container

- Common Production-Level Dockerfile pattern, these steps will makes rebuild faster since Docker Image builds in cached layers:
    - Copy project file (".csproj") first   (If the file does not change afterward, Docker can reuse the restored packages layer aka the OG layer of this step)
    - Restore dependencies
    - Copy source code
    - Build / publish

- Port Consideration:
    - Before this stage:
        - This app runs with "dotnet run" which based on /Properties/launchSettings.json:
            - The server is listening on both ports 5184 & 7278
        - However, when placing this application within a Docker container, that file is not the main thing controlling production-style startup.
        - Thus, we should explicitly tell ASP.NET Core what port to listen on inside the container
    - After this stage:
        - Move local machine port 5184 to the generated Docker container's HTTP port 8080

- Afterward, start building the container image by:
    - 1. "docker build -t url-shortener-api:stage3c ." in the root folder where Dockerfile for this image is located
    - 2. Check if the image is created / existing by: "docker images"
    - 3. "docker run --rm -p 5184:8080 --name url-shortener-api url-shortener-api:stage3c"
    - Where:
        - --rm = remove the container after it stops.
        - -p 5184:8080 = your machine port 5184 mapping -> container port 8080
        - --name url-shortener-api = give the running container a friendly name
        - url-shortener-api:stage3c = name of the image to build the container
    - 4. Test the app inside the created container: "http://localhost:5184/shorten/someCode"
    
### D - Docker Compose API + PostgreSQL together
- Goal:
    - Run the API container and PostgreSQL container together as one Docker Compose project.
    - The API should connect to PostgreSQL through Docker Compose networking.
- Before this stage:
    - PostgreSQL was running in Docker.
    - The API was usually running from the local machine with "dotnet run".
    - Local machine used "localhost:5434" to reach PostgreSQL.
- After this stage:
    - Both "api" and "db" run as containers in the same Compose project.
    - Compose creates a shared internal network for these services.
    - The API reaches PostgreSQL with "Host=db;Port=5432".
- Important Docker networking concept:
    - "localhost" depends on where the code is running.
    - From the local laptop:
        - "localhost:5434" means laptop port 5434, mapped to the PostgreSQL container.
    - From inside the API container:
        - "localhost" means the API container itself.
        - It does not mean the laptop.
        - It does not mean the PostgreSQL container.
    - From inside the API container, use the Compose service name:
        - "db:5432"
- Port mapping:
    - "5434:5432" means:
        - laptop port 5434 -> PostgreSQL container port 5432
    - "5184:8080" means:
        - laptop port 5184 -> API container port 8080
    - Container-to-container communication does not use the laptop port.
    - API container talks to DB container using "db:5432", not "localhost:5434".
- Environment override:
    - The API code still calls:
        - "builder.Configuration.GetConnectionString(\"Postgres\")"
    - In local development, this value can come from "appsettings.json".
    - In Docker Compose, "ConnectionStrings__Postgres" overrides the value.
    - Double underscore "__" represents nested configuration:
        - "ConnectionStrings__Postgres" = "ConnectionStrings:Postgres"
- "depends_on":
    - "depends_on: db" tells Compose to start the database container before the API container.
    - It does not fully guarantee PostgreSQL is ready to accept connections.
    - For this learning project, it is acceptable.
    - Later, production-style systems may use health checks and retry logic.
- Check Comments:
    - Dockerfile.
    - Program.cs regarding HTTPS redirect.
    - Docker/docker-compose.yml on "api" service.

### E - Environment Variables / Production-style Configuration
- Concepts:
    - Code should not need to change when environment changes.
    - Configuration should change instead
- In real backend projects, it is important to separate these followings:
    - Code = app behavior and logic.
    - Config = database host, ports, logging level, environment name.
    - Secrets = passwords, API keys, tokens.
- Before this Stage:
    - "Postgres" connection string is placed within "appsettings.json"
- After this Stage:
    - 1. Introducte ".env" for Docker Compose values.
    - 2. Update Docker Compose file to use variables from ".env" (step above)
- Configuration sources used in this project:
    - "appsettings.json":
        - Default local configuration.
        - Useful when running the API directly with "dotnet run".
    - ".env":
        - Local Docker Compose values for this machine.
        - Ignored by Git because it may contain secrets or machine-specific values.
    - ".env.example":
        - Safe template for other developers.
        - Should include the required variable names.
        - Secret values should use placeholders such as "change_me".
    - Docker Compose "environment":
        - Passes environment variables into containers.
        - Can override values from "appsettings.json".
- Why ".env" is ignored:
    - It may contain passwords, API keys, tokens, or local-only ports.
    - Each developer or environment may need different values.
    - The project should commit ".env.example" instead, so others know which variables are required.
- Why the Compose connection string is built from variables:
    - The "db" service and "api" service share the same source values:
        - POSTGRES_USER
        - POSTGRES_PASSWORD
        - POSTGRES_DB
    - This avoids mismatches where PostgreSQL creates one database but the API tries to connect to another.
    - For local Compose, building the connection string from parts is clear and useful.
    - In cloud/production environments, using one full secret connection string is also common.
- Important port distinction:
    - POSTGRES_HOST_PORT:
        - Used by the laptop to reach PostgreSQL.
        - Example: localhost:5434
    - POSTGRES_CONTAINER_PORT:
        - Used by containers inside the Compose network.
        - Example: db:5432
    - The API connection string should use POSTGRES_CONTAINER_PORT because API -> DB is container-to-container traffic.

## Stage 4 - Redis Cache, RabbitMQ Analytics, Rate Limiting & Cleanup Factor
## A - Redis caching for redirects
- Before this stage: 
    GET /go/{shortCode} -> Query PostgreSQL
                        -> Update analytics
                        -> Redirect based on the retrieved URL
- After this stage:
    GET /go/{shortCode} -> Check Redis first
                        -> If found: use cached URL, else: Query PostgreSQL
                        -> Store URL in Redis
                        -> Redirect based on this storaged URL
- PostgreSQL = this application's source of truth   (permanent truth)
- Redis = a fast temporary memory store             (Fast shortcut)
- Thus, if Redis loses data, the application still work because PostgreSQL still holding the real data.
- Cache hit:
    - Redis already has the destination URL for the short code.
    - The app can skip the PostgreSQL SELECT for the redirect URL.
    - Analytics are still updated in PostgreSQL.
- Cache miss:
    - Redis does not have the destination URL.
    - The app reads from PostgreSQL, then stores the URL in Redis for next time.
- TTL (Time To Live):
    - Cached redirect URLs are stored with an expiration time.
    - In this project, the cached URL lives for 10 minutes.
    - TTL is a safety net so stale data does not live forever.
- Important limitation:
    - Stage 4A does not yet handle cache invalidation when a URL is updated or deleted.
    - That is why Stage 4B exists.
    - Without invalidation, Redis may temporarily return an old URL until the TTL expires.

- Step-by-step taken for this Stage:
    1. Add Redis service to Docker Compose.
    2. Add Redis config to ".env" and ".env.example"
    3. Add Redis package to ASP.NET Core    = "dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis"
        - This package will gives:
            - AddStackExchangeRedisCache(...)
            - IDistributedCache backed by Redis = This let this application use a cache without the "controller" file need to know all Redis detail.
                - "Controller" will depend on cache abstraction, not Redis client details.
                - Later, implementation can change more easily.
    4. Register Redis cache in "Program.cs"
    5. Inject cache into "RedirectController"
    6. Implement cache miss path.
    7. Implement cache hit path.
    8. Add logs for cache hit / miss.
    9. Test redirects twice and observe logs

- Access Redis Container on Docker by = "docker exec -it urlshortener_webapp_Redis redis-cli" & "KEYS *" - to check to stored key & "TTL value" to check time to live remaining of that key; "exit" to exit Redis container
    - In this project, because of using IDistributedCache => the value of the stored key will be stored as a hash with metadata (expiration time) and to access it, following these steps:
        127.0.0.1:6379> TYPE shorturl:ePcTc8
                        hash
        127.0.0.1:6379> HGETALL shorturl:ePcTc8
                        1) "absexp"
                        2) "639185764275311177"
                        3) "sldexp"
                        4) "-1"
                        5) "data"
                        6) "https://www.google.com/"

## B - Cache invalidation on update / delete
- Cache invalidation means removing cached data when the database changes.
- PUT invalidates because the destination URL may have changed.
- DELETE invalidates because the short URL no longer exists.
- Invalidation happens after SaveChangesAsync succeeds.
- After invalidation, the next redirect becomes a cache miss and reloads from PostgreSQL.

## C - Rate Limiting
- Because this application does not have any user / API key, thus:
    - Client IP Address will be used instead
- In "Program.cs", added: using System.Threading.RateLimiting;
    - In ASP.NET Core's built-in rate limiter uses types like:
        - FixedWindowRateLimiterOptions
        - QueueProcessingOrder
        - RateLimitPartition
- In production-level, this app API does not receive traffic directly from the real user, but instead:
    - User -> Load Balancer -> Reverse Proxy -> API Container
    - Thus, RemoteIPAddress = Load Balancer IP but not real user's IP
    - BAD:
        - User A, B, C all appear as same IP.
        - They share one rate limit bucket.
        - One noisy user can cause others to get 429.
    - SOLUTION:
        - Proxies often forward the real client IP in headers such as: "X-Forwarded-For", "X-Real-IP", "Forwarded" 
        - However, these should not be trusted blindly from the public internet, because clients can fake them.
        - Production apps usually configure "Forwarded Headers MiddleWare" & trusted proxies / networks.
            => Only trust X-Forwarded-For if it came from a proxy / load balancer that this app control.

## D - Cleanup / expiring URLS
- Important lessons:
    - Expiration must be enforced before redirect logic.
    - Cache must not bypass expiration rules.
- Expiring URLs = a short URL stops working after a certain time (not the true website URL)
    - Common Reasons why a short URL must be expired:
        - Temporary campaigns.
        - Limited-time promotions.
        - Password reset links.
        - Event Registration links.
        - Private files shares.
        - Demo links.
        - Security-sensitive links.
        - Cleanup old unused data.
- Cleanup = eventually remove expired rows from the database.
- Steps:
    1. Add "ExpiresAt" to the database model.
    2. Add "ExpiresAt" to response DTOs.
    3. Allow create / update request to optionally set "ExpiresAt".
    4. Add validation: expiry must be in the future.
    5. Add migration / database update.
    6. Change redirect logic:
        - If URL is expired, return a non-redirect response.
        - Remove its Redis cache key if needed.
    7. Adjust Redis caching so cached URL does not live longer than the URL expiry
    8. Add cleanup process for expired rows.
- Important Concepts:
    - A Controller runs because an HTTP request came in.
        - AppDbContext = should live only for one small unit of work.
    - A background service runs because the app is live, a.k.a, lives for the whole app lifetime.
        - IServiceScopeFactory = lets the background service create a short-lived DI scope when needed


## E - RabbitMQ async analytics - Decoupling with asynchronous events
- Before this stage: In "UrlRedirectController.cs", redirect analytics happen directly inside the request: 
    await _context.ShortUrls
        .Where(s => s.ShortCode == shortCode)
        .ExecuteUpdateAsync(...)
- After this stage:
    GET /go/{shortCode}
        -> Redis / Postgres finds redirect URL
        -> API publishes "short code was accessed" event to RabbitMQ
        -> API redirects user immediately
    Background worker:
        -> Consumes RabbitMQ event.
        -> Updates AccessCount and LastAccessedAt in PostgreSQL.
- Lesson: Redirect should stay fast, while analytics can happen slightly later.
- Step-by-step:
    1. Add RabbitMQ to Docker Compose.
    2. Add RabbitMQ values to ".env" and ".env.example".
    3. Install RabbitMQ .NET client package.
        - "dotnet add package RabbitMQ.Client", this package gives:
            - ConnectionFactory = builds a connection to RabbitMQ using connection strings in ".env"
            - IConnection = the network connection from this app to RabbitMQ.
            - IChannel = the "conversation lane" used to declare queues, publish messages, and consume messages.
            - BasicPublishAsync = sends a message into RabbitMQ.
            - BasicConsumeAsync = starts listening to a queue.
            - AsyncEventingBasicConsumer = lets this app's background worker run async code whenever a message arrives.
    4. Create a tiny analytics event model.
        - "Models/ShortUrlAccessedEvent.cs"
    5. Create publisher service.
        - "Services/ShortUrlAnalyticsPublisher.cs"
    6. Register the Publisher in DI Container
    7. Inject published into "UrlRedirectController.cs".
    8. Replace direct DB analytics update with publising an event.
        - Check "/Controllers/UrlRedirectController.cs"
    9. Create a background consumer service.
        - "/Services/ShortUrlAnalyticsConsumerService.cs"
    10. Register services in "Program.cs".
    11. Test with redirects and stats.
        - Access RabbitMQ Dashboard = http://localhost:15672
        - Credentials: username = guest
                       password = guest
    12. Fix "Microsoft.OpenApi / NU1903 vulnerability warning": change 10.0.8 to 10.0.9 for <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.9" />
            - And add this line below the above: <PackageReference Include="Microsoft.OpenApi" Version="2.7.5" />

- App's Architecture After this stage:
    - API container = handles HTTP requests.
        - Controllers.
        - Cleanup background service.
        - RabbitMQ analytics consumer background service
            - A more advanced system: would make this into a separate container (analytics-worker container)
    - PostgreSQL container = Permanent source of truth.
    - Redis container = Fast temporary redirect cache.
    - RabbitMQ container = Queue for background analytics events.

# Testing:
- Before Stage 3E, Run the docker: "docker compose -f Docker/docker-compose.yml up -d" or "docker compose up -d" within the Docker folder.
- Or "docker compose -f Docker/docker-compose.yml up --build":
    - docker compose = run services defined in a compose file
    - -f Docker/docker-compose.yml = use this compose file
    - up = create / start the services
    - --build = Before starting containers, build the image for services that have a build section
    - If not including that, Docker Compose may use an existing image / container from an earlier build which may not right
    - Thus, "--build" = rebuilds the images first to make sure these included the latest changes.
    - -d = detached / background mode => Run in the background if included else it will run in the foreground and show logs live
- dotnet run from the Root folder.
- After Stage 3E, Run this command at project root:
    "docker compose --env-file .env -f Docker/docker-compose.yml config" = Safe check, as "config" prints the final resolved Compose configuration after variables are substituted
    "docker compose --env-file .env -f Docker/docker-compose.yml up --build"
    - Output as followings:
            name: url-shortener-project
                services:
                api:
                    build:
                    context: C:\Users\Hayden Duong\Desktop\learning_projects\url_shortening_service
                    dockerfile: Dockerfile
                    container_name: urlshortener_webapp_API
                    depends_on:
                    db:
                        condition: service_started
                        required: true
                    environment:
                    ConnectionStrings__Postgres: Host=db;Port=5432;Database=UrlShortenerDB;Username=postgres;Password=postgres;GSS Encryption Mode=Disable
                    networks:
                    default: null
                    ports:
                    - mode: ingress
                        target: 8080
                        published: "5184"
                        protocol: tcp
                db:
                    container_name: urlshortener_webapp_DB
                    environment:
                    POSTGRES_DB: UrlShortenerDB
                    POSTGRES_PASSWORD: postgres
                    POSTGRES_USER: postgres
                    image: postgres:16
                    networks:
                    default: null
                    ports:
                    - mode: ingress
                        target: 5432
                        published: "5434"
                        protocol: tcp
                    volumes:
                    - type: volume
                        source: pgdata
                        target: /var/lib/postgresql/data
                        volume: {}
                networks:
                default:
                    name: url-shortener-project_default
                volumes:
                pgdata:
                    name: url-shortener-project_pgdata

# Remember:
- API is public to laptop / browser.
- PostgreSQL & Redis are internal services. => Redis can be reachable by the API container, but not exposed directly to local laptop.
