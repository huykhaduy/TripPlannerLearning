using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using TripPlanner.Application.Features.Auth.Dtos;
using Xunit;

namespace TripPlanner.WebApi.Tests;

/// <summary>
/// HTTP-level tests for AuthController — the reference feature slice. These
/// cover what the Application-layer AuthServiceTests can't: real routing,
/// model binding, and ExceptionHandlingMiddleware turning thrown app
/// exceptions into the correct HTTP status/ProblemDetails shape.
/// </summary>
public class AuthEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_ValidRequest_ReturnsUnverifiedUser()
    {
        var client = _factory.CreateClient();
        var email = AuthTestHelper.UniqueEmail("register-ok");

        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, AuthTestHelper.DefaultPassword, "Ada"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(user);
        Assert.Equal(email, user!.Email);
        Assert.False(user.IsEmailVerified);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var email = AuthTestHelper.UniqueEmail("register-dup");
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, AuthTestHelper.DefaultPassword, null));

        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, AuthTestHelper.DefaultPassword, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal((int)HttpStatusCode.Conflict, problem!.Status);
    }

    [Fact]
    public async Task Register_PasswordTooShort_ReturnsBadRequestWithFieldError()
    {
        var client = _factory.CreateClient();
        var email = AuthTestHelper.UniqueEmail("register-short-pw");

        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "short", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.True(problem!.Extensions.ContainsKey("errors"));
    }

    [Fact]
    public async Task Login_BeforeEmailVerified_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        var email = AuthTestHelper.UniqueEmail("login-unverified");
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, AuthTestHelper.DefaultPassword, null));

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, AuthTestHelper.DefaultPassword));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var email = AuthTestHelper.UniqueEmail("login-wrong-pw");
        await _factory.RegisterAndVerifyAsync(client, email);

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "totally-wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_AfterVerification_ReturnsAccessToken()
    {
        var client = _factory.CreateClient();
        var email = AuthTestHelper.UniqueEmail("login-ok");
        await _factory.RegisterAndVerifyAsync(client, email);

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, AuthTestHelper.DefaultPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.False(string.IsNullOrWhiteSpace(auth!.AccessToken));
        Assert.True(auth.User.IsEmailVerified);
    }

    [Fact]
    public async Task VerifyEmail_InvalidToken_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest("not-a-real-token"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
