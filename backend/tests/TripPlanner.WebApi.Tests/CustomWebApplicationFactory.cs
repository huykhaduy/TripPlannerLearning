using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Infrastructure.Persistence;

namespace TripPlanner.WebApi.Tests;

/// <summary>
/// Boots the real <see cref="Program"/> for HTTP-level tests, with two swaps:
/// (1) the Postgres-backed <see cref="ApplicationDbContext"/> is replaced by a
/// fresh EF Core InMemory database per factory instance, so tests need no
/// Docker/Postgres; (2) required-secret config (Jwt:Key, ConnectionStrings:Postgres)
/// that Program.cs reads directly at startup — before any WebApplicationFactory
/// hook gets a chance to override it — is supplied as real process environment
/// variables in the constructor, the same mechanism a real .env file uses.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "test-only-signing-key-do-not-use-in-real-environments-12345";

    // Captured once per factory instance and reused inside the options lambda
    // below. AddDbContext's optionsLifetime defaults to Scoped (not Singleton),
    // so the lambda re-runs once per DI scope (i.e. once per HTTP request, and
    // again for every factory.Services.CreateScope() a test opens) — inlining
    // Guid.NewGuid() directly in the lambda would hand every scope its own
    // fresh, empty database instead of one shared per factory instance.
    private readonly string _databaseName = $"WebApiTests-{Guid.NewGuid()}";

    public CustomWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("Jwt__Key", TestJwtKey);
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Postgres",
            "Host=localhost;Database=unused;Username=unused;Password=unused");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // AddDbContext<T> doesn't just register DbContextOptions<T> — since
            // EF Core 5 it also registers the options-building action itself as a
            // composable IDbContextOptionsConfiguration<T>, so that multiple
            // AddDbContext<T> calls layer their configuration together instead of
            // replacing each other. Removing only DbContextOptions<T> leaves that
            // configuration entry behind, so Program's UseNpgsql(...) action still
            // runs alongside ours below — both providers end up configured on the
            // same options, which EF Core rejects. Both must be removed.
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));

            // Geoapify/Serper are typed HttpClients hitting real external APIs —
            // swapped for deterministic fakes so destination-related tests don't
            // depend on network access or a real API key (which is unset here).
            services.RemoveAll<IDestinationProvider>();
            services.AddSingleton<IDestinationProvider, FakeDestinationProvider>();

            services.RemoveAll<IImageSearchProvider>();
            services.AddSingleton<IImageSearchProvider, FakeImageSearchProvider>();
        });
    }
}
