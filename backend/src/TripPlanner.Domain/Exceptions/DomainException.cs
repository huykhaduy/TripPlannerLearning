namespace TripPlanner.Domain.Exceptions;

/// <summary>
/// Thrown when a domain invariant / business rule is violated
/// (for example: trip start date is after the end date).
/// The Web API layer translates this into an HTTP 400 response.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
