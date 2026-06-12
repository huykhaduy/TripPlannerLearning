using Microsoft.EntityFrameworkCore;
using Moq;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Features.Auth;
using TripPlanner.Application.Features.Auth.Dtos;
using TripPlanner.Domain.Entities;
using TripPlanner.Infrastructure.Identity;
using TripPlanner.Infrastructure.Persistence;
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

    private static AuthService CreateSut(ApplicationDbContext db)
    {
        // Real BCrypt hasher (cheap enough for tests); fake token generator.
        var hasher = new BCryptPasswordHasher();

        var tokenGenerator = new Mock<IJwtTokenGenerator>();
        tokenGenerator
            .Setup(t => t.GenerateToken(It.IsAny<User>()))
            .Returns(("fake-jwt", DateTimeOffset.UtcNow.AddHours(1)));

        return new AuthService(db, hasher, tokenGenerator.Object);
    }

    [Fact]
    public async Task RegisterAsync_WithNewEmail_CreatesUserAndReturnsToken()
    {
        using var db = CreateDb();
        var sut = CreateSut(db);

        var result = await sut.RegisterAsync(new RegisterRequest("New@Example.com", "password123", "Newbie"));

        Assert.Equal("fake-jwt", result.AccessToken);
        Assert.Equal("new@example.com", result.User.Email); // normalised to lower-case
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.NotEqual("password123", db.Users.Single().PasswordHash); // stored as a hash
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
        await sut.RegisterAsync(new RegisterRequest("user@example.com", "password123", null));

        var result = await sut.LoginAsync(new LoginRequest("user@example.com", "password123"));

        Assert.Equal("fake-jwt", result.AccessToken);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ThrowsUnauthorized()
    {
        using var db = CreateDb();
        var sut = CreateSut(db);
        await sut.RegisterAsync(new RegisterRequest("user@example.com", "password123", null));

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            sut.LoginAsync(new LoginRequest("user@example.com", "wrong-password")));
    }
}
