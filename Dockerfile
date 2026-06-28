# Dockerizing the API app into a Docker container

# I. Build Phase: SDK Image (Kitchen)
# Use Microsoft's .NET SDK image as the first stage and named this stage as "build"
# This SDK image includes tools like: dotnet restore, dotnet build, dotnet publish
# A Docker image is like a packaged filesystem + metadata.
# During "docker build", Docker starts temporary containers for each build step
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build         

# This command will create / set a folder inside the temporary build container
# Think this as: "This is the folder inside the image / container where we work during this stage"
# Thus, there will be another folder for Runtime Stage
# Convention: 
#       /src = source code / build area
#       /app = final published app area
# If "/src" does not exist yet -> Docker will creates it
# Similar to: "mkdir /src" + "cd /src" inside the container
WORKDIR /src

# This file controls NuGet dependencies
COPY url_shortening_service.csproj .

# Docker runs this command from inside "/src"
RUN dotnet restore

# Copy source code and Publish
# Copy the rest of the source code (this app folder) from local into the container's current working directory and ingore files written in .dockerignore
# "From project root on host -> /src inside build stage"
COPY . .

# Build the app container for deployment
#   dotnet publish = compile & prepare runnable app output
#   -c Release = use Release configuration, not Debug
#   -o /app/publish = put published output into /app/publish
#   --no-restore = do not restore packages again because we already did it (the above step)
# Remember: when the app runs in production stage, it does not need all source files from the source code folder that we "fed" it with "COPY . ."
# It only needs the compiled result which is a cleaner runnable folder, something like:
# /app/publish/
#   url_shortening_service.dll
#   url_shortening_service.deps.json
#   url_shortening_service.runtimeconfig.json
#   appsettings.json
#   appsettings.Development.json
#   Npgsql.EntityFrameworkCore.PostgreSQL.dll
#   Microsoft.EntityFrameworkCore.dll
#   other dependency files...
# Instead of this folder structure, which is more files = more heavy and no all files will be needed to make the app running from the Docker container
# In other words:
#   1. Take my source code, copied from local machine, in /src
#   2. Compile it
#   3. Collect the files needed to run the application
#   4. Put those and store them into /app/publish
# Analogy: cooked meal box that ready to serve
RUN dotnet publish -c Release -o /app/publish --no-restore


# II. Runtime Phase: Runtime Image (delivery container)
# Start a new final image using the ASP.NET runtime image:
#   This image can run ASP.NET apps, but it does not include the full SDK tools.
#   Production containers should usually be smaller & contain only what they need to run.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Use /app as the final app folder.
WORKDIR /app

# Copy only the published app output from the "build" stage into the final "runtime" stage
# From the "build" stage, take the finished runnable app files stored in /app/publish folder
# Copy them into the final runtime image's /app folder (stated in WORKDIR /app) instead from /src
COPY --from=build /app/publish .

# Since we are moving the application into a Docker container, the original ports stated in /Properties/launchSettings.json will no longer control this container scenario.
# This tells ASP.NET Core to listen for HTTP traffic on port 8080 inside the created container.
# ENV ASPNETCORE_URLS=http://+:8080 - still possible but the the below is more suitable for the newer .NET container style.
ENV ASPNETCORE_HTTP_PORTS=8080

# This is not yet "exposing", just a metadata for documents the container port
# This line is more of: "This app is expected to listen on port 8080"
# The actual mapping happens later with "docker run -p 5184:8080 url-shortener-api", which is the actual command that connects the local machine to the container
# When other people download this container down into their machine, they will use the above command, "docker run -p HOST_PORT:CONTAINER_PORT this_container_image_name" to connect
EXPOSE 8080

# Docker will runs this command when the container starts
# The file name is present in the output from "RUN dotnet publish ..." above
# This file is the compiled application
ENTRYPOINT [ "dotnet", "url_shortening_service.dll" ]
