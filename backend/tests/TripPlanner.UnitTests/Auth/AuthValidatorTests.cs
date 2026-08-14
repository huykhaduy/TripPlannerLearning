using FluentValidation.TestHelper;
using TripPlanner.Application.Features.Auth.Dtos;
using TripPlanner.Application.Features.Auth.Validators;
using Xunit;

namespace TripPlanner.UnitTests.Auth;

/// <summary>
/// Rule-level tests for the Auth validators. AuthServiceTests already proves that
/// invalid input throws, so these exist for what it does NOT check: which PROPERTY
/// carries the error (the frontend highlights fields from it), the exact MESSAGE
/// (several are deliberately identical so they can't be used to probe an account),
/// and the BOUNDARY values either side of each limit.
/// </summary>
public class AuthValidatorTests
{
    private static readonly RegisterRequestValidator Register = new();
    private static readonly ResendVerificationRequestValidator Resend = new();
    private static readonly LoginRequestValidator Login = new();

    private const string EmailRequiredMessage = "A valid email address is required.";

    // -----------------------------------------------------------------
    // RegisterRequestValidator
    // -----------------------------------------------------------------

    [Fact]
    public void Register_WithValidDetails_HasNoErrors()
    {
        var result = Register.TestValidate(new RegisterRequest("user@example.com", "password123", "Duy"));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    public void Register_WithAnUnusableEmail_ReportsTheSameMessageOnEmail(string? email)
    {
        var result = Register.TestValidate(new RegisterRequest(email!, "password123", null));

        // One message for "missing" AND "malformed" on purpose: a different message per
        // case would tell an attacker which check they tripped.
        result.ShouldHaveValidationErrorFor(x => x.Email).WithErrorMessage(EmailRequiredMessage);
    }

    [Fact]
    public void Register_WithPasswordAtExactlyTheMinimum_HasNoPasswordError()
    {
        var atMinimum = new string('x', RegisterRequestValidator.MinPasswordLength);

        var result = Register.TestValidate(new RegisterRequest("user@example.com", atMinimum, null));

        result.ShouldNotHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Register_WithPasswordOneCharBelowTheMinimum_ReportsPasswordError()
    {
        var tooShort = new string('x', RegisterRequestValidator.MinPasswordLength - 1);

        var result = Register.TestValidate(new RegisterRequest("user@example.com", tooShort, null));

        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage($"Password must be at least {RegisterRequestValidator.MinPasswordLength} characters.");
    }

    [Fact]
    public void Register_WithNullPassword_ReportsTheLengthMessageRatherThanThrowing()
    {
        // CascadeMode.Stop matters here: without it, MinimumLength would run against
        // null after NotEmpty already failed.
        var result = Register.TestValidate(new RegisterRequest("user@example.com", null!, null));

        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage($"Password must be at least {RegisterRequestValidator.MinPasswordLength} characters.");
    }

    [Fact]
    public void Register_WithNoDisplayName_IsValid()
    {
        // DisplayName is optional (AuthService normalises blank to null).
        var result = Register.TestValidate(new RegisterRequest("user@example.com", "password123", null));

        result.ShouldNotHaveAnyValidationErrors();
    }

    // -----------------------------------------------------------------
    // ResendVerificationRequestValidator
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-at-sign")]
    public void Resend_WithAnUnusableEmail_ReportsTheSameMessageAsRegister(string? email)
    {
        var result = Resend.TestValidate(new ResendVerificationRequest(email!));

        result.ShouldHaveValidationErrorFor(x => x.Email).WithErrorMessage(EmailRequiredMessage);
    }

    // -----------------------------------------------------------------
    // LoginRequestValidator — deliberately looser than Register
    // -----------------------------------------------------------------

    /// <summary>
    /// The important one. Login must NOT re-apply the registration password policy:
    /// raising MinPasswordLength later would otherwise lock out every existing
    /// account whose password predates the change, and the 400 would announce the new
    /// rule while doing it. If someone "tidies up" by adding MinimumLength here, this
    /// test is what stops them.
    /// </summary>
    [Fact]
    public void Login_WithAPasswordShorterThanRegisterAllows_IsStillValid()
    {
        var shorterThanRegisterAllows = new string('x', RegisterRequestValidator.MinPasswordLength - 1);

        var result = Login.TestValidate(new LoginRequest("user@example.com", shorterThanRegisterAllows));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Login_WithAnEmailMissingTheAtSign_IsStillValid()
    {
        // Whether the address exists is answered by looking it up, with one generic
        // 401 — the shape of the string is not login's business.
        var result = Login.TestValidate(new LoginRequest("no-at-sign", "password123"));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Login_WithABlankEmail_ReportsEmailRequired(string? email)
    {
        var result = Login.TestValidate(new LoginRequest(email!, "password123"));

        result.ShouldHaveValidationErrorFor(x => x.Email).WithErrorMessage("Email is required.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Login_WithABlankPassword_ReportsPasswordRequired(string? password)
    {
        var result = Login.TestValidate(new LoginRequest("user@example.com", password!));

        result.ShouldHaveValidationErrorFor(x => x.Password).WithErrorMessage("Password is required.");
    }
}
