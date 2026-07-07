namespace TripPlanner.Application.Common.Exceptions;

/// <summary>
/// Thrown when incoming data fails business validation (maps to HTTP 400).
/// Carries a field -> error-messages dictionary so the API can return a
/// structured ValidationProblemDetails response.
/// </summary>
public class ValidationException : Exception
{
    public ValidationException(string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public IDictionary<string, string[]> Errors { get; }

    /// <summary>
    /// Convenience for the common single-field case in service-layer business
    /// rules (rules that need the DB and therefore can't live in a validator).
    /// </summary>
    public static ValidationException ForProperty(string propertyName, string message) =>
        new(new Dictionary<string, string[]> { [propertyName] = [message] });
}
