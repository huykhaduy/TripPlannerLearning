namespace TripPlanner.Application.Common.Exceptions;

/// <summary>
/// Thrown when authentication fails, e.g. invalid email/password
/// (maps to HTTP 401). The message is intentionally generic to avoid
/// account-enumeration (Feature 4 / US1 business rule).
/// </summary>
public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message)
    {
    }
}
