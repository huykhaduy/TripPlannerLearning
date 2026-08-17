using Microsoft.Extensions.DependencyInjection;
using TripPlanner.Application.Features.Auth;
using TripPlanner.Application.Features.Destinations;
using TripPlanner.Application.Features.Trips;

namespace TripPlanner.Application;

/// <summary>
/// Registers Application-layer services in the DI container.
/// Called from the Web API's Program.cs: <c>builder.Services.AddApplication();</c>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITripService, TripService>();
        services.AddScoped<IDestinationService, DestinationService>();

        // Validators are NOT registered: they are stateless, dependency-free rule
        // declarations, so each service holds its own shared instances instead of
        // taking an IValidator<T> per request type. See AuthService for the reasoning.

        // Wall clock as a dependency so cache-freshness logic is testable
        // (tests substitute a fake TimeProvider and fast-forward time).
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
