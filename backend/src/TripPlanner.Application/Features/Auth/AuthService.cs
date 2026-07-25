using System.Net.Mail;
using System.Net.Sockets;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
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
///   * depend on INTERFACES from the Application layer (IApplicationDbContext,
///     IPasswordHasher, IJwtTokenGenerator) — never on EF/Infrastructure types;
///   * validate input and enforce business rules, throwing the Application
///     exceptions (Validation/Conflict/Unauthorized) that the API maps to HTTP;
///   * map entities to DTOs so we never leak the password hash to the client.
///
/// Use it as the blueprint for TripService and DestinationService.
/// </summary>
public class AuthService : IAuthService
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _tokenGenerator;
    private readonly IEmailSender _emailSender;
    private readonly IAppUrlProvider _appUrls;
    private readonly ICurrentUserService _currentUser;
    private readonly IValidator<RegisterRequest> _registerValidator;

    public AuthService(
        IApplicationDbContext db,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator tokenGenerator,
        IEmailSender emailSender,
        IAppUrlProvider appUrls,
        ICurrentUserService currentUser,
        IValidator<RegisterRequest> registerValidator)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _emailSender = emailSender;
        _appUrls = appUrls;
        _currentUser = currentUser;
        _registerValidator = registerValidator;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        // Input rules live in RegisterRequestValidator (Feature 4 / US1);
        // failures surface as our ValidationException -> HTTP 400.
        await _registerValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

        var email = NormalizeEmail(request.Email);

        // Business rule: email must be unique.
        var emailTaken = await _db.Users.AnyAsync(u => u.Email == email, cancellationToken);
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

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        // Registration still succeeds even if the email itself can't be sent
        // (SMTP down/misconfigured) — the user can retry via "resend
        // verification email"; a mail outage shouldn't block sign-up.
        await SendVerificationEmailAsync(user, cancellationToken);

        return BuildAuthResponse(user);
    }

    public async Task VerifyEmailAsync(string token, CancellationToken cancellationToken = default)
    {
        var userId = _tokenGenerator.ValidateEmailVerificationToken(token)
            ?? throw new ValidationException("This verification link is invalid or has expired.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), userId);

        if (!user.IsEmailVerified)
        {
            user.IsEmailVerified = true;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ResendVerificationEmailAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetRequiredUserId();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), userId);

        if (user.IsEmailVerified)
        {
            return; // nothing to resend
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
        var email = NormalizeEmail(request.Email);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        // Verify even when the user is missing? We short-circuit here for clarity.
        // The error is deliberately the same for "no such user" and "wrong password".
        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("Invalid email or password.");
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
