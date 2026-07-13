using FluentValidation;
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

        // Registers every AbstractValidator<T> in this assembly as IValidator<T>.
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        // Wall clock as a dependency so cache-freshness logic is testable
        // (tests substitute a fake TimeProvider and fast-forward time).
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
