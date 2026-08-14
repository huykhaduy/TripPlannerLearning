using FluentValidation;
using TripPlanner.Application.Common.Validation;
using Xunit;
using ApplicationValidationException = TripPlanner.Application.Common.Exceptions.ValidationException;

namespace TripPlanner.Application.Tests.Common;

/// <summary>
/// The bridge every feature method's first line goes through. FluentValidation has
/// a ValidationException of its own that ExceptionHandlingMiddleware knows nothing
/// about — if this extension ever let that one escape, every 400 in the API would
/// turn into a 500 with no field errors, and no service test would notice because
/// they all assert on the project's type.
/// </summary>
public class ValidationExtensionsTests
{
    private sealed record Request(string? Email, string? Password);

    private sealed class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(r => r.Email).NotEmpty().WithMessage("Email is required.");
            RuleFor(r => r.Password)
                .NotEmpty().WithMessage("Password is required.")
                .MinimumLength(8).WithMessage("Password must be at least 8 characters.");
        }
    }

    private readonly RequestValidator _validator = new();

    [Fact]
    public async Task WhenTheInstanceIsValid_ItReturnsWithoutThrowing()
    {
        await _validator.ValidateAndThrowAppExceptionAsync(new Request("ada@example.com", "correct horse"));
    }

    [Fact]
    public async Task WhenTheInstanceIsInvalid_ItThrowsTheProjectsValidationException()
    {
        // Specifically not FluentValidation.ValidationException — the middleware
        // only maps this type to 400, and anything else falls through to 500.
        await Assert.ThrowsAsync<ApplicationValidationException>(
            () => _validator.ValidateAndThrowAppExceptionAsync(new Request(null, "short")));
    }

    [Fact]
    public async Task ItDoesNotLeakFluentValidationsOwnExceptionType()
    {
        var ex = await Record.ExceptionAsync(
            () => _validator.ValidateAndThrowAppExceptionAsync(new Request(null, null)));

        Assert.IsNotType<FluentValidation.ValidationException>(ex);
    }

    [Fact]
    public async Task ItGroupsFailuresByPropertyName()
    {
        var ex = await Assert.ThrowsAsync<ApplicationValidationException>(
            () => _validator.ValidateAndThrowAppExceptionAsync(new Request(null, "short")));

        Assert.Equal(["Email is required."], ex.Errors["Email"]);
        Assert.Equal(["Password must be at least 8 characters."], ex.Errors["Password"]);
    }

    [Fact]
    public async Task ItKeepsSeveralMessagesForOneProperty()
    {
        // The dictionary value is string[], not string — the API's "errors" object
        // is meant to render every problem with a field at once.
        var validator = new InlineValidator<Request>();
        validator.RuleFor(r => r.Password).NotEmpty().WithMessage("Password is required.");
        validator.RuleFor(r => r.Password).MinimumLength(8).WithMessage("Password must be at least 8 characters.");

        var ex = await Assert.ThrowsAsync<ApplicationValidationException>(
            () => validator.ValidateAndThrowAppExceptionAsync(new Request("ada@example.com", "")));

        Assert.Equal(2, ex.Errors["Password"].Length);
    }

    [Fact]
    public async Task ItCarriesTheGenericTopLevelMessage()
    {
        // The per-field detail lives in Errors; the message is deliberately generic
        // because it is what ends up in ProblemDetails.detail for every 400.
        var ex = await Assert.ThrowsAsync<ApplicationValidationException>(
            () => _validator.ValidateAndThrowAppExceptionAsync(new Request(null, null)));

        Assert.Equal("One or more validation errors occurred.", ex.Message);
    }

    [Fact]
    public async Task ItPassesTheCancellationTokenToTheValidator()
    {
        var validator = new InlineValidator<Request>();
        validator.RuleFor(r => r.Email)
            .MustAsync(async (_, ct) =>
            {
                await Task.Yield();
                ct.ThrowIfCancellationRequested();
                return true;
            });

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // An async rule that hits the database is exactly the case that would one
        // day justify moving a validator into DI; it has to be cancellable.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => validator.ValidateAndThrowAppExceptionAsync(new Request("a@b.c", "correct horse"), cts.Token));
    }
}
