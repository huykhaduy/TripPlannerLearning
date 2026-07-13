using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Infrastructure.ExternalApis;
using TripPlanner.Infrastructure.Identity;
using TripPlanner.Infrastructure.Persistence;

namespace TripPlanner.Infrastructure;

/// <summary>
/// Registers Infrastructure implementations for the Application interfaces.
/// Called from Program.cs: <c>builder.Services.AddInfrastructure(builder.Configuration);</c>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        AddPersistence(services, configuration);

        // Options pattern: bind the "Jwt" section to JwtSettings.
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

        // Security primitives.
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

        // In-memory cache for browse-path provider results (spec §11.2 NFR1/NFR2).
        services.AddMemoryCache();

        // External travel data provider (typed HttpClient + bound settings).
        services.Configure<GeoapifySettings>(configuration.GetSection(GeoapifySettings.SectionName));
        services.AddHttpClient<IDestinationProvider, GeoapifyClient>(client =>
        {
            var baseUrl = configuration["Geoapify:BaseUrl"] ?? "https://api.geoapify.com/";
            client.BaseAddress = new Uri(baseUrl);
        });

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        // Switch providers from configuration: "Database:Provider" = "Sqlite" (default) or "Postgres".
        var provider = configuration["Database:Provider"] ?? "Sqlite";

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
            {
                options.UseNpgsql(configuration.GetConnectionString("Postgres"));
            }
            else
            {
                options.UseSqlite(configuration.GetConnectionString("Sqlite") ?? "Data Source=tripplanner.db");
            }
        });

        // Expose the context to the Application layer through its interface.
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
    }
}
