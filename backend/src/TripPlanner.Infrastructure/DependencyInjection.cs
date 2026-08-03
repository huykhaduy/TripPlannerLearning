using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Infrastructure.Caching;
using TripPlanner.Infrastructure.Email;
using TripPlanner.Infrastructure.ExternalApis;
using TripPlanner.Infrastructure.Identity;
using TripPlanner.Infrastructure.Persistence;
using TripPlanner.Infrastructure.Persistence.Repositories;

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
        services.AddSingleton<IAppUrlProvider, AppUrlProvider>();

        // Email (F4/US2 verification link) — Gmail SMTP relay.
        services.Configure<SmtpSettings>(configuration.GetSection(SmtpSettings.SectionName));
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        // Cache backend for browse-path provider results (spec §11.2 NFR1/NFR2),
        // switchable from configuration: "Cache:Provider" = "Memory" (default) or
        // "Redis". Both branches register IDistributedCache, decorated by
        // ResilientDistributedCache — DestinationService depends only on that
        // abstraction and never sees StackExchange.Redis's exception types.
        AddCaching(services, configuration);

        // External travel data provider (typed HttpClient + bound settings).
        // Explicit timeout: search/attractions/details all sit in the request
        // path a user is actively waiting on, so a hung Geoapify call shouldn't
        // be allowed to ride the ~100s HttpClient default.
        // RemoveAllLoggers(): Geoapify's API takes apiKey as a query-string parameter
        // (it has no header-based auth option), and HttpClientFactory's built-in
        // logging handlers log the full request URI at Information level by
        // default — without this, every search/attractions/details call would
        // write the live API key to whatever log sink is configured.
        services.Configure<GeoapifySettings>(configuration.GetSection(GeoapifySettings.SectionName));
        services.AddHttpClient<IDestinationProvider, GeoapifyClient>(client =>
        {
            var baseUrl = configuration["Geoapify:BaseUrl"] ?? "https://api.geoapify.com/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(10);
        }).RemoveAllLoggers();

        // Image search, used to fill in attraction thumbnails the destination
        // provider itself doesn't return (typed HttpClient + bound settings).
        // Short timeout: this runs up to MaxDegreeOfParallelism-at-a-time per
        // attraction in a list, so one hung call shouldn't hold up the batch
        // anywhere near the 100s HttpClient default.
        services.Configure<SerperSettings>(configuration.GetSection(SerperSettings.SectionName));
        services.AddHttpClient<IImageSearchProvider, SerperImageClient>(client =>
        {
            var baseUrl = configuration["Serper:BaseUrl"] ?? "https://google.serper.dev/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(8);
        });

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres")));

        // Generic repository + unit of work (open generic covers IRepository<ItineraryDay>,
        // IRepository<ItineraryItem> directly; entity-specific repositories are
        // registered individually as they're introduced in later tasks).
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IDestinationRepository, DestinationRepository>();
        services.AddScoped<ITripRepository, TripRepository>();
    }

    private static void AddCaching(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Cache:Provider"] ?? "Memory";

        if (provider.Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = configuration.GetConnectionString("Redis");
                options.InstanceName = "TripPlanner:";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        // Decorate whichever IDistributedCache was just registered above with
        // ResilientDistributedCache, so a connectivity failure (Redis
        // down/unreachable, or a bare timeout) degrades to a cache miss/no-op
        // instead of throwing.
        var registration = services.Single(d => d.ServiceType == typeof(IDistributedCache));
        services.Remove(registration);
        services.AddSingleton<IDistributedCache>(sp =>
        {
            var real = (IDistributedCache)(registration.ImplementationFactory?.Invoke(sp)
                ?? ActivatorUtilities.CreateInstance(sp, registration.ImplementationType!));
            return new ResilientDistributedCache(real);
        });
    }
}
