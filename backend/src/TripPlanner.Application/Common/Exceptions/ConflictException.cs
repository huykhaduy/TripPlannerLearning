namespace TripPlanner.Application.Common.Exceptions;

/// <summary>
/// Thrown when a request conflicts with current state (maps to HTTP 409).
/// Example: registering with an email that is already in use.
/// </summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }
}
