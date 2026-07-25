namespace TripPlanner.Application.Common.Exceptions;

/// <summary>
/// Thrown by <see cref="Interfaces.IUnitOfWork.SaveChangesAsync"/> when a save
/// fails because of a concurrent write (e.g. a unique-index violation). This
/// is NOT one of the exceptions ExceptionHandlingMiddleware maps to an HTTP
/// status — callers must catch it and translate it into a feature-specific
/// exception (usually ConflictException) with a message appropriate to what
/// they were trying to do.
/// </summary>
public class ConcurrencyException : Exception
{
    public ConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
