using FluentValidation;
using TripPlanner.Application.Features.Auth.Dtos;

namespace TripPlanner.Application.Features.Auth.Validators;

/// <summary>
/// Input validation for login (Feature 4 / US3). Deliberately far looser than
/// <see cref="RegisterRequestValidator"/> — presence checks only.
///
/// Login must NOT re-apply the registration policy. If
/// <see cref="RegisterRequestValidator.MinPasswordLength"/> is ever raised,
/// re-checking it here would lock out every existing account whose (perfectly
/// valid) password predates the change, and the 400 would announce the new
/// rule while doing it. Whether the credentials are correct is decided by
/// looking them up, which answers with a single generic 401.
///
/// The job here is narrow: stop a null/blank field from reaching
/// <c>AuthService.LoginAsync</c>, where <c>NormalizeEmail</c> would dereference
/// it and turn a malformed request into a 500 instead of a 400.
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
