using System.Net.Mail;
using System.Net.Sockets;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Common.Validation;
using TripPlanner.Application.Features.Auth.Dtos;
using TripPlanner.Application.Features.Auth.Validators;
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
    /// <summary>
    /// Deliberately vague, and deliberately shared by both rejection paths (the
    /// in-memory check and the unique-index backstop): a caller must not be able to
    /// tell "this email is taken" from anything else, or registration becomes an
    /// account-enumeration oracle (F4/US1).
    /// </summary>
    private const string RegistrationRejectedMessage = "Unable to register with the provided details.";

    // Validators are stateless rule declarations with no dependencies of their own, so
    // they are shared instances rather than constructor parameters. Injecting
    // IValidator<T> would mean an interface with exactly one implementation that
    // nothing ever substitutes — the tests construct these same classes directly.
    // FluentValidation validators are safe to reuse concurrently once built.
    private static readonly RegisterRequestValidator RegisterValidator = new();
    private static readonly ResendVerificationRequestValidator ResendValidator = new();
    private static readonly LoginRequestValidator LoginValidator = new();

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _tokenGenerator;
    private readonly IEmailSender _emailSender;
    private readonly IAppUrlProvider _appUrls;

    public AuthService(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator tokenGenerator,
        IEmailSender emailSender,
        IAppUrlProvider appUrls)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _emailSender = emailSender;
        _appUrls = appUrls;
    }

    public async Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        // Input rules live in RegisterRequestValidator (Feature 4 / US1);
        // failures surface as our ValidationException -> HTTP 400.
        await RegisterValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

        var email = NormalizeEmail(request.Email);

        // Business rule: email must be unique.
        var emailTaken = await _users.ExistsByEmailAsync(email, cancellationToken);
        if (emailTaken)
        {
            throw new ConflictException(RegistrationRejectedMessage);
        }

        var user = new User
        {
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
            IsEmailVerified = false, // F4/US2 — flipped by VerifyEmailAsync once the emailed link is opened.
        };

        try
        {
            await _users.AddAsync(user, cancellationToken);
        }
        catch (ConcurrencyException)
        {
            // Unique index on User.Email: the ExistsByEmailAsync check above is a
            // read-then-write, so a concurrent registration for the same address can
            // land between the two. Without this the loser got an unmapped
            // ConcurrencyException and the caller saw a 500 instead of a 409.
            throw new ConflictException(RegistrationRejectedMessage);
        }

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
        await ResendValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

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
            // F4/US1: the account still exists and resending is a separate,
            // retryable step, so a mail outage must not fail registration.
            // Already logged by the IEmailSender implementation — swallowing the
            // exception here loses no diagnostic information.
        }
    }

    private static bool IsTransientEmailFailure(Exception ex) =>
        ex is SmtpException or SocketException; // SmtpFailedRecipientException derives from SmtpException

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        // Presence only — LoginRequestValidator explains why login deliberately
        // does not re-apply the registration password policy. Without this, a
        // null email reached NormalizeEmail below and 500'd instead of 400'd.
        await LoginValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

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
