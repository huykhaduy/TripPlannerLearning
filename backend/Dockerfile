# Build context: backend/ — supplied by docker-compose.deploy.yml, which sets
# `context: ./backend` from the repo root. Coolify's "Base Directory" is therefore "/",
# not "backend"; see docs/deployment-coolify.md.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, on just the project files, so this layer caches across
# source-only changes.
COPY Directory.Build.props ./
COPY src/TripPlanner.Domain/TripPlanner.Domain.csproj src/TripPlanner.Domain/
COPY src/TripPlanner.Application/TripPlanner.Application.csproj src/TripPlanner.Application/
COPY src/TripPlanner.Infrastructure/TripPlanner.Infrastructure.csproj src/TripPlanner.Infrastructure/
COPY src/TripPlanner.WebApi/TripPlanner.WebApi.csproj src/TripPlanner.WebApi/
RUN dotnet restore src/TripPlanner.WebApi/TripPlanner.WebApi.csproj

COPY src/ src/
RUN dotnet publish src/TripPlanner.WebApi/TripPlanner.WebApi.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Solely so docker-compose.deploy.yml's healthcheck has something to run: the aspnet
# runtime image ships neither curl nor wget. ~5MB; the list cleanup keeps it there.
# If the /health healthcheck is ever dropped, drop this too.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Coolify routes traffic to this port; TLS termination happens upstream
# (matches Program.cs, which never forces UseHttpsRedirection itself).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "TripPlanner.WebApi.dll"]
