using FluentValidation;
using TripPlanner.Application.Features.Auth.Dtos;

namespace TripPlanner.Application.Features.Auth.Validators;

/// <summary>
/// Presence checks only (Feature 4 / US3). Login must NOT re-apply
/// <see cref="RegisterRequestValidator"/>'s password policy — raising that
/// policy later would lock out existing accounts whose passwords predate it.
/// Whether credentials are correct is answered by a generic 401 instead.
/// </summary>
public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}
