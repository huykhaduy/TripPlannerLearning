using FluentValidation;
using TripPlanner.Application.Features.Auth.Dtos;

namespace TripPlanner.Application.Features.Auth.Validators;

/// <summary>
/// Input validation for the anonymous resend-verification endpoint
/// (Feature 4 / US2). Same email shape check as <see cref="RegisterRequestValidator"/>.
/// </summary>
public class ResendVerificationRequestValidator : AbstractValidator<ResendVerificationRequest>
{
    public ResendVerificationRequestValidator()
    {
        RuleFor(x => x.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A valid email address is required.")
            .Must(email => email.Contains('@')).WithMessage("A valid email address is required.");
    }
}
