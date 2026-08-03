using Microsoft.EntityFrameworkCore;
using Moq;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Features.Auth;
using TripPlanner.Application.Features.Auth.Dtos;
using TripPlanner.Application.Features.Auth.Validators;
using TripPlanner.Domain.Entities;
using TripPlanner.Infrastructure.Identity;
using TripPlanner.Infrastructure.Persistence;
using TripPlanner.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TripPlanner.Application.Tests.Auth;

/// <summary>
/// Unit tests for the reference Auth slice. These double as a worked example of
/// the testing pattern students should follow for their own services:
///   * build a fresh in-memory database per test (Arrange);
///   * exercise one behaviour (Act);
///   * assert the outcome (Assert).
/// </summary>
public class AuthServiceTests
{
    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    private static AuthService CreateSut(
        ApplicationDbContext db,
        Mock<IEmailSender>? emailSender = null,
        IUserRepository? userRepository = null)
    {
        // Real repository by default; tests that need to simulate a persistence-level
        // failure (e.g. a unique-index violation) pass their own.
        var users = userRepository ?? new UserRepository(db);

        // Real BCrypt hasher (cheap enough for tests); fake token generator.
        var hasher = new BCryptPasswordHasher();

        var tokenGenerator = new Mock<IJwtTokenGenerator>();
        tokenGenerator
            .Setup(t => t.GenerateToken(It.IsAny<User>()))
            .Returns(("fake-jwt", DateTimeOffset.UtcNow.AddHours(1)));
        // Deterministic fake "token": round-trips back to the same user id via
        // ValidateEmailVerificationToken; any other string is "invalid".
        tokenGenerator
            .Setup(t => t.GenerateEmailVerificationToken(It.IsAny<User>()))
            .Returns<User>(u => $"verify-token-for-{u.Id}");
        tokenGenerator
            .Setup(t => t.ValidateEmailVerificationToken(It.IsAny<string>()))
            .Returns<string>(token =>
                token.StartsWith("verify-token-for-", StringComparison.Ordinal)
                    ? Guid.Parse(token["verify-token-for-".Length..])
                    : null);

        var email = emailSender ?? new Mock<IEmailSender>();

        var appUrls = new Mock<IAppUrlProvider>();
        appUrls.Setup(p => p.FrontendBaseUrl).Returns("http://localhost:5173");

        // Validators are not injected — AuthService holds its own shared instances,
        // so the real rules always run here.
        return new AuthService(users, hasher, tokenGenerator.Object, email.Object, appUrls.Object);
    }

    [Fact]
    public async Task RegisterAsync_WithNewEmail_CreatesUserButDoesNotLogIn()
    {
        using var db = CreateDb();
        var sut = CreateSut(db);

        var result = await sut.RegisterAsync(new RegisterRequest("New@Example.com", "password123", "Newbie"));

        Assert.Equal("new@example.com", result.Email); // normalised to lower-case
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.NotEqual("password123", db.Users.Single().PasswordHash); // stored as a hash
        Assert.False(result.IsEmailVerified); // F4/US2 — starts unverified; no session is issued
    }

    [Fact]
    public async Task RegisterAsync_SendsAVerificationEmail()
    {
        using var db = CreateDb();
        var emailSender = new Mock<IEmailSender>();
        var sut = CreateSut(db, emailSender: emailSender);

        await sut.RegisterAsync(new RegisterRequest("new@example.com", "password123", null));

        emailSender.Verify(
            e => e.SendAsync("new@example.com", It.IsAny<string>(), It.Is<string>(body => body.Contains("verify-token-for-")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_WhenEmailSendingFails_StillCreatesTheAccount()
    {
        using var db = CreateDb();
        var emailSender = new Mock<IEmailSender>();
        emailSender
            .Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Net.Mail.SmtpException("simulated SMTP outage"));
        var sut = CreateSut(db, emailSender: emailSender);

        var result = await sut.RegisterAsync(new RegisterRequest("new@example.com", "password123", null));

        Assert.Equal("new@example.com", result.Email); // registration succeeds regardless
        Assert.Equal(1, await db.Users.CountAsync());
    }

    /// <summary>
    /// F4/US1 — `User.Email` is a DB-level unique index and the duplicate check above
    /// the insert is a read-then-write, so two simultaneous registrations for the same
    /// address can both pass the check. The loser must get the same generic 409 as any
    /// other duplicate, NOT a 500 from an unmapped ConcurrencyException.
    ///
    /// The conflict is simulated at the repository boundary because the EF Core
    /// InMemory provider does not enforce unique indexes at all.
    /// </summary>
    [Fact]
    public async Task RegisterAsync_WhenAConcurrentRegistrationWinsTheRace_ThrowsConflict()
    {
        using var db = CreateDb();
        var users = new Mock<IUserRepository>();
        users
            .Setup(u => u.ExistsByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // the check passes — the other request hasn't committed yet
        users
            .Setup(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyException("simulated unique violation", new InvalidOperationException()));
        var sut = CreateSut(db, userRepository: users.Object);

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            sut.RegisterAsync(new RegisterRequest("race@example.com", "password123", null)));

        // Identical to the ordinary duplicate path — otherwise the difference between
        // the two would leak whether the address was already registered.
        Assert.Equal("Unable to register with the provided details.", error.Message);
    }

    [Fact]
    public async Task RegisterAsync_WithDuplicateEmail_ThrowsConflict()
    {
        using var db = CreateDb();
        var sut = CreateSut(db);
        await sut.RegisterAsync(new RegisterRequest("dupe@example.com", "password123", null));

        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.RegisterAsync(new RegisterRequest("dupe@example.com", "password123", null)));
    }

    [Fact]
    public async Task RegisterAsync_WithShortPassword_ThrowsValidation()
    {
        using var db = CreateDb();
        var sut = CreateSut(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.RegisterAsync(new RegisterRequest("user@example.com", "short", null)));
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsToken()
    {
        using var db = CreateDb();
        var sut = CreateSut(db);
        var registered = await sut.RegisterAsync(new RegisterRequest("user@example.com", "password123", null));
        await sut.VerifyEmailAsync($"verify-token-for-{registered.Id}");

        var result = await sut.LoginAsync(new LoginRequest("user@example.com", "password123"));

        Assert.Equal("fake-jwt", result.AccessToken);
        Assert.True(result.User.IsEmailVerified);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ThrowsUnauthorized_EvenThoughUnverified()
    {
        // The account is unverified (registration's default) here on purpose —
        // wrong-password must still get the generic 401, not leak the
        // unverified-account 403, regardless of verification status.
        using var db = CreateDb();
        var sut = CreateSut(db);
        await sut.RegisterAsync(new RegisterRequest("user@example.com", "password123", null));

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            sut.LoginAsync(new LoginRequest("user@example.com", "wrong-password")));
    }

    [Fact]
    public async Task LoginAsync_WithUnverifiedEmail_ThrowsForbidden()
    {
        // F4/US2 — login is blocked until the account is verified.
        using var db = CreateDb();
        var sut = CreateSut(db);
        await sut.RegisterAsync(new RegisterRequest("user@example.com", "password123", null));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.LoginAsync(new LoginRequest("user@example.com", "password123")));
    }

    /// <summary>
    /// F4/US3 — a blank or missing field is a malformed request (400), not a
    /// failed credential check (401). Before LoginRequestValidator existed, a
    /// null email reached NormalizeEmail and threw NullReferenceException,
    /// which the middleware could only report as a 500.
    /// </summary>
    [Theory]
    [InlineData(null, "password123")]
    [InlineData("", "password123")]
    [InlineData("   ", "password123")]
    [InlineData("user@example.com", null)]
    [InlineData("user@example.com", "")]
    public async Task LoginAsync_WithMissingCredentials_ThrowsValidation(string? email, string? password)
    {
        using var db = CreateDb();
        var sut = CreateSut(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.LoginAsync(new LoginRequest(email!, password!)));
    }

    [Fact]
    public async Task VerifyEmailAsync_WithValidToken_MarksTheUserVerified()
    {
        using var db = CreateDb();
        var sut = CreateSut(db);
        var registered = await sut.RegisterAsync(new RegisterRequest("user@example.com", "password123", null));
        var token = $"verify-token-for-{registered.Id}";

        await sut.VerifyEmailAsync(token);

        Assert.True((await db.Users.SingleAsync()).IsEmailVerified);
    }

    [Fact]
    public async Task VerifyEmailAsync_WithInvalidToken_ThrowsValidation()
    {
        using var db = CreateDb();
        var sut = CreateSut(db);

        await Assert.ThrowsAsync<ValidationException>(() => sut.VerifyEmailAsync("garbage-token"));
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_WhenUnverified_SendsAnotherEmail()
    {
        using var db = CreateDb();
        var emailSender = new Mock<IEmailSender>();
        var sut = CreateSut(db, emailSender: emailSender);
        await sut.RegisterAsync(new RegisterRequest("user@example.com", "password123", null));
        emailSender.Invocations.Clear(); // ignore the email RegisterAsync already sent

        await sut.ResendVerificationEmailAsync(new ResendVerificationRequest("user@example.com"));

        emailSender.Verify(
            e => e.SendAsync("user@example.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_WhenAlreadyVerified_DoesNotSendAnEmail()
    {
        using var db = CreateDb();
        var emailSender = new Mock<IEmailSender>();
        var sut = CreateSut(db, emailSender: emailSender);
        var registered = await sut.RegisterAsync(new RegisterRequest("user@example.com", "password123", null));
        await sut.VerifyEmailAsync($"verify-token-for-{registered.Id}");
        emailSender.Invocations.Clear();

        await sut.ResendVerificationEmailAsync(new ResendVerificationRequest("user@example.com"));

        emailSender.Verify(
            e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_WithUnknownEmail_DoesNotThrowAndSendsNoEmail()
    {
        // An anonymous endpoint must not reveal whether an email is registered.
        using var db = CreateDb();
        var emailSender = new Mock<IEmailSender>();
        var sut = CreateSut(db, emailSender: emailSender);

        await sut.ResendVerificationEmailAsync(new ResendVerificationRequest("nobody@example.com"));

        emailSender.Verify(
            e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
