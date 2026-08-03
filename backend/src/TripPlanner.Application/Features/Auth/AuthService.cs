using System.Net.Mail;
using System.Net.Sockets;
using FluentValidation;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Common.Validation;
using TripPlanner.Application.Features.Auth.Dtos;
using TripPlanner.Domain.Entities;
using ValidationException = TripPlanner.Application.Common.Exceptions.ValidationException;

namespace TripPlanner.Application.Features.Auth;

/// <summary>
/// ============================================================================
/// REFERENCE IMPLEMENTATION — read this carefully.
/// ============================================================================
/// This is the one feature slice that is fully built out. It demonstrates the
/// shape every other use-case in this project should follow:
///
///   * depend on INTERFACES from the Application layer (IUserRepository,
///     IPasswordHasher, IJwtTokenGenerator) — never on EF/Infrastructure types;
///   * validate input and enforce business rules, throwing the Application
///     exceptions (Validation/Conflict/Unauthorized) that the API maps to HTTP;
///   * map entities to DTOs so we never leak the password hash to the client.
///
/// Use it as the blueprint for TripService and DestinationService.
/// </summary>
public class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _tokenGenerator;
    private readonly IEmailSender _emailSender;
    private readonly IAppUrlProvider _appUrls;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<ResendVerificationRequest> _resendValidator;
    private readonly IValidator<LoginRequest> _loginValidator;

    public AuthService(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator tokenGenerator,
        IEmailSender emailSender,
        IAppUrlProvider appUrls,
        IValidator<RegisterRequest> registerValidator,
        IValidator<ResendVerificationRequest> resendValidator,
        IValidator<LoginRequest> loginValidator)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _emailSender = emailSender;
        _appUrls = appUrls;
        _registerValidator = registerValidator;
        _resendValidator = resendValidator;
        _loginValidator = loginValidator;
    }

    public async Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        // Input rules live in RegisterRequestValidator (Feature 4 / US1);
        // failures surface as our ValidationException -> HTTP 400.
        await _registerValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

        var email = NormalizeEmail(request.Email);

        // Business rule: email must be unique.
        var emailTaken = await _users.ExistsByEmailAsync(email, cancellationToken);
        if (emailTaken)
        {
            // Generic message — do not reveal whether the email exists (avoids
            // account enumeration, Feature 4 / US1).
            throw new ConflictException("Unable to register with the provided details.");
        }

        var user = new User
        {
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
            IsEmailVerified = false, // F4/US2 — flipped by VerifyEmailAsync once the emailed link is opened.
        };

        await _users.AddAsync(user, cancellationToken);

        // Registration still succeeds even if the email itself can't be sent
        // (SMTP down/misconfigured) — the user can retry via "resend
        // verification email"; a mail outage shouldn't block sign-up.
        await SendVerificationEmailAsync(user, cancellationToken);

        // No JWT here — F4/US2 blocks login until the account is verified, so
        // handing back a working session at registration would bypass that gate.
        return user.ToDto();
    }

    public async Task VerifyEmailAsync(string token, CancellationToken cancellationToken = default)
    {
        var userId = _tokenGenerator.ValidateEmailVerificationToken(token)
            ?? throw new ValidationException("This verification link is invalid or has expired.");

        var user = await _users.GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), userId);

        if (!user.IsEmailVerified)
        {
            user.IsEmailVerified = true;
            await _users.UpdateAsync(user, cancellationToken);
        }
    }

    public async Task ResendVerificationEmailAsync(ResendVerificationRequest request, CancellationToken cancellationToken = default)
    {
        await _resendValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

        var user = await _users.GetByEmailAsync(NormalizeEmail(request.Email), cancellationToken);

        // Same outcome whether the email doesn't exist, is already verified, or
        // is unverified — this is an anonymous endpoint (login now requires a
        // verified email, so there's no session to resend from), and it must
        // not leak which emails are registered.
        if (user is null || user.IsEmailVerified)
        {
            return;
        }

        await SendVerificationEmailAsync(user, cancellationToken);
    }

    private async Task SendVerificationEmailAsync(User user, CancellationToken cancellationToken)
    {
        var token = _tokenGenerator.GenerateEmailVerificationToken(user);
        var link = $"{_appUrls.FrontendBaseUrl}/verify-email?token={Uri.EscapeDataString(token)}";
        var greetingName = user.DisplayName is null ? "" : $" {user.DisplayName}";

        try
        {
            await _emailSender.SendAsync(
                user.Email,
                "Verify your TripPlanner email",
                $"<p>Hi{greetingName},</p>"
                    + "<p>Click below to verify your email and activate your account:</p>"
                    + $"<p><a href=\"{link}\">{link}</a></p>"
                    + "<p>This link expires in 24 hours.</p>",
                cancellationToken);
        }
        catch (Exception ex) when (IsTransientEmailFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            // SMTP down, misconfigured, or the recipient was rejected — the
            // account still exists; resending is a separate, retryable step.
        }
    }

    private static bool IsTransientEmailFailure(Exception ex) =>
        ex is SmtpException or SocketException; // SmtpFailedRecipientException derives from SmtpException

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        // Presence only — LoginRequestValidator explains why login deliberately
        // does not re-apply the registration password policy. Without this, a
        // null email reached NormalizeEmail below and 500'd instead of 400'd.
        await _loginValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

        var email = NormalizeEmail(request.Email);

        var user = await _users.GetByEmailAsync(email, cancellationToken);

        // Verify even when the user is missing? We short-circuit here for clarity.
        // The error is deliberately the same for "no such user" and "wrong password".
        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("Invalid email or password.");
        }

        // Only reveal "unverified" once the password is confirmed correct —
        // otherwise this would leak account existence to a wrong-password guess.
        if (!user.IsEmailVerified)
        {
            throw new ForbiddenException("Please verify your email before logging in.");
        }

        return BuildAuthResponse(user);
    }

    private AuthResponse BuildAuthResponse(User user)
    {
        var (token, expiresAt) = _tokenGenerator.GenerateToken(user);
        return new AuthResponse(token, expiresAt, user.ToDto());
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
