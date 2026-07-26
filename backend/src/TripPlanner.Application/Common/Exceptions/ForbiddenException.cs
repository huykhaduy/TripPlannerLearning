namespace TripPlanner.Application.Common.Exceptions;

/// <summary>
/// Thrown when the caller is correctly identified but not allowed to perform
/// the action (maps to HTTP 403) — distinct from <see cref="UnauthorizedException"/>,
/// which means the caller's identity/credentials themselves are invalid.
/// Example: login with the correct password but an unverified email
/// (Feature 4 / US2).
/// </summary>
public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message)
    {
    }
}
