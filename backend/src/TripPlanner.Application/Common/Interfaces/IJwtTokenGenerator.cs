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
}
