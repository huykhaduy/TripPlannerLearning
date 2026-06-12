using TripPlanner.Application.Features.Auth.Dtos;

namespace TripPlanner.Application.Features.Auth;

/// <summary>
/// Authentication use-cases (Feature 4). This is the FULLY IMPLEMENTED reference
/// slice — study <see cref="AuthService"/> to learn the pattern, then build the
/// Trips and Destinations services the same way.
/// </summary>
public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
}
