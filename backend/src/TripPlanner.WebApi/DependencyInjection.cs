using TripPlanner.Application.Common.Interfaces;
using TripPlanner.WebApi.Services;

namespace TripPlanner.WebApi;

/// <summary>
/// Registers Web API-only implementations of Application interfaces — things
/// that depend on the ASP.NET Core HTTP pipeline itself (HttpContext), so they
/// can't live in Infrastructure alongside the EF/JWT/BCrypt/external-API
/// implementations. Called from Program.cs: <c>builder.Services.AddWebApi();</c>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddWebApi(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        return services;
    }
}
