using FluentValidation;

namespace TripPlanner.Application.Common.Validation;

/// <summary>
/// Bridges FluentValidation to this project's exception model. FluentValidation
/// has its own <c>ValidationException</c>, but the Web API's
/// <c>ExceptionHandlingMiddleware</c>
/// only knows the Application-layer <see cref="Exceptions.ValidationException"/> —
/// this extension converts a failed validation result into the latter, so the
/// API keeps returning the same structured 400 responses.
/// </summary>
public static class ValidationExtensions
{
    public static async Task ValidateAndThrowAppExceptionAsync<T>(
        this IValidator<T> validator,
        T instance,
        CancellationToken cancellationToken = default)
    {
        var result = await validator.ValidateAsync(instance, cancellationToken);
        if (!result.IsValid)
        {
            // ToDictionary() groups failures by property name -> string[] of messages,
            // exactly the shape our ValidationException carries.
            throw new Exceptions.ValidationException(result.ToDictionary());
        }
    }
}
