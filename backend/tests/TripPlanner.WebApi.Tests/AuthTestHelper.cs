using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Features.Auth.Dtos;

namespace TripPlanner.WebApi.Tests;

/// <summary>
/// Drives the real register/verify/login HTTP flow for tests that need an
/// authenticated user, without ever sending a real email: the verification
/// token is minted directly via <see cref="IJwtTokenGenerator"/> (resolved
/// from the factory's DI container), the same way the emailed link's token
/// would be produced — <see cref="TripPlanner.Infrastructure.Email.SmtpEmailSender"/>
/// itself silently no-ops when unconfigured, so no email is ever sent or needed.
/// </summary>
public static class AuthTestHelper
{
    public const string DefaultPassword = "password123!";

    public static async Task<Guid> RegisterAndVerifyAsync(
        this CustomWebApplicationFactory factory, HttpClient client, string email, string password = DefaultPassword)
    {
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password, null));
        registerResponse.EnsureSuccessStatusCode();
        var user = (await registerResponse.Content.ReadFromJsonAsync<UserDto>())!;

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var tokenGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();
        var entity = (await users.GetByIdAsync(user.Id))!;
        var verificationToken = tokenGenerator.GenerateEmailVerificationToken(entity);

        var verifyResponse = await client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(verificationToken));
        verifyResponse.EnsureSuccessStatusCode();

        return user.Id;
    }

    public static async Task<string> LoginAsync(HttpClient client, string email, string password = DefaultPassword)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        return auth.AccessToken;
    }

    /// <summary>Registers, verifies, and logs in a fresh user, returning an HttpClient with the JWT already attached.</summary>
    public static async Task<(HttpClient Client, Guid UserId)> CreateAuthenticatedClientAsync(
        this CustomWebApplicationFactory factory, string email, string password = DefaultPassword)
    {
        var client = factory.CreateClient();
        var userId = await factory.RegisterAndVerifyAsync(client, email, password);
        var token = await LoginAsync(client, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, userId);
    }

    public static string UniqueEmail(string label) => $"{label}-{Guid.NewGuid():N}@example.com";
}
