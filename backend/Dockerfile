# Build context: backend/ (Coolify "Base Directory" = backend)
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
COPY --from=build /app/publish .

# Coolify routes traffic to this port; TLS termination happens upstream
# (matches Program.cs, which never forces UseHttpsRedirection itself).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "TripPlanner.WebApi.dll"]
