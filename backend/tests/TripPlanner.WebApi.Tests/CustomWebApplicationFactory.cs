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
/// Postgres becomes a fresh EF Core InMemory database per factory instance, and
/// the secrets Program.cs reads at startup are set as environment variables —
/// the constructor is the only hook that runs early enough to beat it.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "test-only-signing-key-do-not-use-in-real-environments-12345";

    // Must be a field, not inlined into the options lambda below: AddDbContext's
    // optionsLifetime is Scoped, so the lambda re-runs per DI scope and every
    // request would otherwise get its own empty database.
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
            // Both registrations must go. AddDbContext also registers its options
            // action as a composable IDbContextOptionsConfiguration<T>, so removing
            // only DbContextOptions<T> leaves Program's UseNpgsql in place and EF
            // Core rejects having two providers on the same options.
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
