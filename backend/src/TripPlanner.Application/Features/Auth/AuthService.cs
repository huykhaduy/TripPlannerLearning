using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Common.Validation;
using TripPlanner.Application.Features.Auth.Dtos;
using TripPlanner.Domain.Entities;

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
    private readonly IValidator<RegisterRequest> _registerValidator;

    public AuthService(
        IApplicationDbContext db,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator tokenGenerator,
        IValidator<RegisterRequest> registerValidator)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
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
            IsEmailVerified = true, // Template simplification; real flow is left as an exercise (US2).
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        return BuildAuthResponse(user);
    }

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
