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

    /// <summary>F4/US2 — flips IsEmailVerified once the emailed link is opened.</summary>
    Task VerifyEmailAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>F4/US2 — re-sends the verification email for the current (authenticated) user.</summary>
    Task ResendVerificationEmailAsync(CancellationToken cancellationToken = default);
}
