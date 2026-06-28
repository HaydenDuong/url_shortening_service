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
    
### D - Docker Compose API + PostgresSQL together
    - Check Comments:
        - Dockerfile.
        - Program.cs regard HTTPS redirect.
        - Docker/docker-compose.yml on "api" service 

### E - Environment Variables / Production-style Configuration

## Stage 4 - Redis Cache, RabbitMQ Analytics, Rate Limiting & Cleanup Factor

# Testing:
- Run the docker: "docker compose -f Docker/docker-compose.yml up -d" or "docker compose up -d" within the Docker folder.
- Or "docker compose -f Docker/docker-compose.yml up --build":
    - docker compose = run services defined in a compose file
    - -f Docker/docker-compose.yml = use this compose file
    - up = create / start the services
    - --build = Before starting containers, build the image for services that have a build section
    - If not including that, Docker Compose may use an existing image / container from an earlier build which may not right
    - Thus, "--build" = rebuilds the images first to make sure these included the latest changes.
    - -d = detached / background mode => Run in the background if included else it will run in the foreground and show logs live
- dotnet run from the Root folder.
