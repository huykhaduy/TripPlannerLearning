namespace TripPlanner.Application.Features.Auth.Dtos;

/// <summary>Feature 4 / US1 — sign up with email and password.</summary>
public record RegisterRequest(string Email, string Password, string? DisplayName);

/// <summary>Feature 4 / US3 — log in with email and password.</summary>
public record LoginRequest(string Email, string Password);

/// <summary>The signed-in user as returned to the client (never includes the hash).</summary>
public record UserDto(Guid Id, string Email, string? DisplayName);

/// <summary>Returned by register/login — the JWT plus the user it belongs to.</summary>
public record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);
