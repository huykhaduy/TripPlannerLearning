using FluentValidation;
using TripPlanner.Application.Features.Auth.Dtos;

namespace TripPlanner.Application.Features.Auth.Validators;

/// <summary>
/// Input validation for registration (Feature 4 / US1). Replaces the manual
/// checks that used to live in <see cref="AuthService"/>. Business rules that
/// need the database (email uniqueness) stay in the service.
/// </summary>
public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public const int MinPasswordLength = 8;

    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .Cascade(CascadeMode.Stop) // don't run Contains('@') on a null email
            .NotEmpty().WithMessage("A valid email address is required.")
            .Must(email => email.Contains('@')).WithMessage("A valid email address is required.");

        RuleFor(x => x.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage($"Password must be at least {MinPasswordLength} characters.")
            .MinimumLength(MinPasswordLength).WithMessage($"Password must be at least {MinPasswordLength} characters.");
    }
}
