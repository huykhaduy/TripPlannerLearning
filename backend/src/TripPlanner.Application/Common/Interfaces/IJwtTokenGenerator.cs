using TripPlanner.Domain.Entities;

namespace TripPlanner.Application.Common.Interfaces;

/// <summary>
/// Issues signed JWT access tokens for authenticated users.
/// Implemented in Infrastructure using the configured signing key.
/// </summary>
public interface IJwtTokenGenerator
{
    /// <summary>Returns a signed JWT and the moment it expires (UTC).</summary>
    (string Token, DateTimeOffset ExpiresAt) GenerateToken(User user);

    /// <summary>
    /// F4/US2 — a short-lived, purpose-scoped token proving control of the
    /// account's email. Signed with the same key as access tokens but issued
    /// for a distinct audience (and carries a distinct purpose claim), so it
    /// is rejected outright by the Web API's normal JWT Bearer authentication
    /// (which only accepts the standard access-token audience) — this token
    /// cannot be used to authenticate as the user against any [Authorize]
    /// endpoint, and an access token can't be replayed here either.
    /// </summary>
    string GenerateEmailVerificationToken(User user);

    /// <summary>
    /// Validates a token from <see cref="GenerateEmailVerificationToken"/> and
    /// returns the user id it was issued for, or null if the token is
    /// missing/expired/malformed/wrong-audience/wrong-purpose.
    /// </summary>
    Guid? ValidateEmailVerificationToken(string token);
}
