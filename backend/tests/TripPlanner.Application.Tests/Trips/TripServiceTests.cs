using Microsoft.EntityFrameworkCore;
using Moq;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Features.Trips;
using TripPlanner.Application.Features.Trips.Dtos;
using TripPlanner.Application.Features.Trips.Validators;
using TripPlanner.Infrastructure.Persistence;
using Xunit;

namespace TripPlanner.Application.Tests.Trips;

public class TripServiceTests
{
    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    private static TripService CreateSut(ApplicationDbContext db, Guid? userId)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(c => c.UserId).Returns(userId);

        return new TripService(db, currentUser.Object, new CreateTripRequestValidator());
    }

    [Fact]
    public async Task CreateTripAsync_WithValidName_CreatesTripForCurrentUser()
    {
        using var db = CreateDb();
        var userId = Guid.NewGuid();
        var sut = CreateSut(db, userId);

        var result = await sut.CreateTripAsync(new CreateTripRequest("  Summer in Da Nang  "));

        Assert.Equal("Summer in Da Nang", result.Name); // trimmed
        Assert.Equal(0, result.DestinationCount);

        var saved = await db.Trips.SingleAsync();
        Assert.Equal(userId, saved.UserId); // NFR 6: owned by the caller
        Assert.Equal(result.Id, saved.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateTripAsync_WithBlankName_ThrowsValidation(string name)
    {
        using var db = CreateDb();
        var sut = CreateSut(db, Guid.NewGuid());

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.CreateTripAsync(new CreateTripRequest(name)));
        Assert.Equal(0, await db.Trips.CountAsync()); // nothing persisted
    }

    [Fact]
    public async Task CreateTripAsync_WhenAnonymous_ThrowsUnauthorized()
    {
        using var db = CreateDb();
        var sut = CreateSut(db, userId: null);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            sut.CreateTripAsync(new CreateTripRequest("Weekend trip")));
    }
}
