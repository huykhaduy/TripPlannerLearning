namespace TripPlanner.Application.Features.Auth.Dtos;

/// <summary>Feature 4 / US1 — sign up with email and password.</summary>
public record RegisterRequest(string Email, string Password, string? DisplayName);

/// <summary>Feature 4 / US3 — log in with email and password.</summary>
public record LoginRequest(string Email, string Password);

/// <summary>The signed-in user as returned to the client (never includes the hash).</summary>
public record UserDto(Guid Id, string Email, string? DisplayName, bool IsEmailVerified);

/// <summary>Feature 4 / US2 — verify the email address behind a registration.</summary>
public record VerifyEmailRequest(string Token);

/// <summary>Feature 4 / US2 — request a fresh verification link for an unverified account.</summary>
public record ResendVerificationRequest(string Email);

/// <summary>Returned by register/login — the JWT plus the user it belongs to.</summary>
public record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);
