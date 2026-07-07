using Microsoft.EntityFrameworkCore;
using Moq;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Features.Destinations.Dtos;
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

    private static TripService CreateSut(ApplicationDbContext db, Guid? userId, IDestinationProvider? provider = null)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(c => c.UserId).Returns(userId);

        return new TripService(db, currentUser.Object, provider ?? Mock.Of<IDestinationProvider>(),
            new CreateTripRequestValidator(), new UpdateTripRequestValidator(),
            new AddDestinationRequestValidator());
    }

    /// <summary>Provider stub that knows one place; returns null for anything else.</summary>
    private static Mock<IDestinationProvider> ProviderKnowing(string providerId, string name)
    {
        var provider = new Mock<IDestinationProvider>();
        provider
            .Setup(p => p.GetDestinationDetailsAsync(providerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DestinationDetailsDto(
                providerId, name, null, null, null, null, null, null, null, null));
        return provider;
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
    public async Task GetMyTripsAsync_ReturnsOnlyCurrentUsersTrips_NewestFirst()
    {
        using var db = CreateDb();
        var me = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();

        await CreateSut(db, me).CreateTripAsync(new CreateTripRequest("Older trip"));
        await CreateSut(db, me).CreateTripAsync(new CreateTripRequest("Newer trip"));
        await CreateSut(db, someoneElse).CreateTripAsync(new CreateTripRequest("Not my trip"));

        var result = await CreateSut(db, me).GetMyTripsAsync();

        Assert.Equal(2, result.Count); // NFR 6: the other user's trip is absent
        Assert.Equal(new[] { "Newer trip", "Older trip" }, result.Select(t => t.Name));
    }

    [Fact]
    public async Task GetTripAsync_ReturnsDaysAndSavedPlaces()
    {
        using var db = CreateDb();
        var me = Guid.NewGuid();
        var sut = CreateSut(db, me);
        var trip = await sut.CreateTripAsync(new CreateTripRequest("Hue trip"));

        var day = new Domain.Entities.ItineraryDay
        {
            TripId = trip.Id, Date = new DateOnly(2026, 8, 1), DayNumber = 1,
        };
        var pagoda = new Domain.Entities.Destination { ProviderId = "geo-1", Name = "Thien Mu Pagoda" };
        var citadel = new Domain.Entities.Destination { ProviderId = "geo-2", Name = "Imperial Citadel" };
        db.ItineraryDays.Add(day);
        db.ItineraryItems.AddRange(
            // scheduled into day 1, deliberately added out of order
            new() { TripId = trip.Id, Destination = pagoda, ItineraryDayId = day.Id, SortOrder = 2 },
            new() { TripId = trip.Id, Destination = citadel, ItineraryDayId = day.Id, SortOrder = 1 },
            // unscheduled -> Saved Places
            new() { TripId = trip.Id, Destination = new() { ProviderId = "geo-3", Name = "Perfume River" }, SortOrder = 1 });
        await db.SaveChangesAsync();

        var result = await sut.GetTripAsync(trip.Id);

        var resultDay = Assert.Single(result.Days);
        Assert.Equal( // ordered by SortOrder, not insertion order
            new[] { "Imperial Citadel", "Thien Mu Pagoda" },
            resultDay.Destinations.Select(d => d.Name));
        Assert.Equal("Perfume River", Assert.Single(result.SavedPlaces).Name);
    }

    [Fact]
    public async Task GetTripAsync_WhenTripBelongsToAnotherUser_ThrowsNotFound()
    {
        using var db = CreateDb();
        var owner = await CreateSut(db, Guid.NewGuid()).CreateTripAsync(new CreateTripRequest("Private trip"));

        var stranger = CreateSut(db, Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() => stranger.GetTripAsync(owner.Id));
    }

    [Fact]
    public async Task UpdateTripAsync_SettingDates_GeneratesOneNumberedDayPerDate()
    {
        using var db = CreateDb();
        var sut = CreateSut(db, Guid.NewGuid());
        var trip = await sut.CreateTripAsync(new CreateTripRequest("Hoi An"));

        var result = await sut.UpdateTripAsync(trip.Id, new UpdateTripRequest(
            "Hoi An", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 3)));

        Assert.Equal(3, result.Days.Count);
        Assert.Equal(new[] { 1, 2, 3 }, result.Days.Select(d => d.DayNumber));
        Assert.Equal(new DateOnly(2026, 8, 1), result.Days[0].Date);
    }

    [Fact]
    public async Task UpdateTripAsync_ShrinkingRange_KeepsDaysAndReturnsOrphanedItemsToSavedPlaces()
    {
        using var db = CreateDb();
        var sut = CreateSut(db, Guid.NewGuid());
        var trip = await sut.CreateTripAsync(new CreateTripRequest("Hue"));
        var detail = await sut.UpdateTripAsync(trip.Id, new UpdateTripRequest(
            "Hue", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 3)));

        // Schedule one destination into day 3 (which is about to fall out of range).
        db.ItineraryItems.Add(new()
        {
            TripId = trip.Id,
            Destination = new() { ProviderId = "geo-1", Name = "Thien Mu Pagoda" },
            ItineraryDayId = detail.Days[2].Id,
        });
        await db.SaveChangesAsync();

        var result = await sut.UpdateTripAsync(trip.Id, new UpdateTripRequest(
            "Hue", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 2)));

        Assert.Equal(2, result.Days.Count);
        Assert.Equal(detail.Days[0].Id, result.Days[0].Id); // kept, not recreated
        Assert.Equal("Thien Mu Pagoda", Assert.Single(result.SavedPlaces).Name);
    }

    [Fact]
    public async Task UpdateTripAsync_StartAfterEnd_ThrowsDomainException()
    {
        using var db = CreateDb();
        var sut = CreateSut(db, Guid.NewGuid());
        var trip = await sut.CreateTripAsync(new CreateTripRequest("Backwards"));

        await Assert.ThrowsAsync<Domain.Exceptions.DomainException>(() =>
            sut.UpdateTripAsync(trip.Id, new UpdateTripRequest(
                "Backwards", new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 1))));
    }

    [Fact]
    public async Task UpdateTripAsync_RangeOverAYear_ThrowsValidation()
    {
        using var db = CreateDb();
        var sut = CreateSut(db, Guid.NewGuid());
        var trip = await sut.CreateTripAsync(new CreateTripRequest("Sabbatical"));

        await Assert.ThrowsAsync<ValidationException>(() =>
            sut.UpdateTripAsync(trip.Id, new UpdateTripRequest(
                "Sabbatical", new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 10))));
    }

    [Fact]
    public async Task AddDestinationAsync_NewProviderId_CachesDestinationAndAddsToSavedPlaces()
    {
        using var db = CreateDb();
        var provider = ProviderKnowing("geo-1", "Golden Bridge");
        var sut = CreateSut(db, Guid.NewGuid(), provider.Object);
        var trip = await sut.CreateTripAsync(new CreateTripRequest("Da Nang"));

        var result = await sut.AddDestinationAsync(trip.Id, new AddDestinationRequest("geo-1", null));

        Assert.Equal("Golden Bridge", result.Name);
        Assert.Equal(0, result.SortOrder);
        Assert.Equal(1, await db.Destinations.CountAsync()); // cache row created
    }

    [Fact]
    public async Task AddDestinationAsync_AlreadyCached_DoesNotCallProvider()
    {
        using var db = CreateDb();
        db.Destinations.Add(new() { ProviderId = "geo-1", Name = "Golden Bridge" });
        await db.SaveChangesAsync();

        var provider = new Mock<IDestinationProvider>(MockBehavior.Strict); // any call would throw
        var sut = CreateSut(db, Guid.NewGuid(), provider.Object);
        var trip = await sut.CreateTripAsync(new CreateTripRequest("Da Nang"));

        var result = await sut.AddDestinationAsync(trip.Id, new AddDestinationRequest("geo-1", null));

        Assert.Equal("Golden Bridge", result.Name);
        Assert.Equal(1, await db.Destinations.CountAsync()); // still one cache row
    }

    [Fact]
    public async Task AddDestinationAsync_SameDestinationTwiceInSameBucket_ThrowsConflict()
    {
        using var db = CreateDb();
        var sut = CreateSut(db, Guid.NewGuid(), ProviderKnowing("geo-1", "Golden Bridge").Object);
        var trip = await sut.CreateTripAsync(new CreateTripRequest("Da Nang"));
        await sut.AddDestinationAsync(trip.Id, new AddDestinationRequest("geo-1", null));

        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.AddDestinationAsync(trip.Id, new AddDestinationRequest("geo-1", null)));
    }

    [Fact]
    public async Task AddDestinationAsync_UnknownProviderId_ThrowsNotFound()
    {
        using var db = CreateDb();
        var sut = CreateSut(db, Guid.NewGuid()); // default provider: returns null
        var trip = await sut.CreateTripAsync(new CreateTripRequest("Da Nang"));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.AddDestinationAsync(trip.Id, new AddDestinationRequest("geo-unknown", null)));
    }

    [Fact]
    public async Task AddDestinationAsync_DayOfAnotherTrip_ThrowsValidation()
    {
        using var db = CreateDb();
        var sut = CreateSut(db, Guid.NewGuid(), ProviderKnowing("geo-1", "Golden Bridge").Object);
        var tripA = await sut.CreateTripAsync(new CreateTripRequest("Trip A"));
        var tripB = await sut.UpdateTripAsync(
            (await sut.CreateTripAsync(new CreateTripRequest("Trip B"))).Id,
            new UpdateTripRequest("Trip B", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1)));

        await Assert.ThrowsAsync<ValidationException>(() => // day belongs to B, trip is A
            sut.AddDestinationAsync(tripA.Id, new AddDestinationRequest("geo-1", tripB.Days[0].Id)));
    }

    [Fact]
    public async Task AddDestinationAsync_SortOrderIncrementsWithinBucket()
    {
        using var db = CreateDb();
        var provider = ProviderKnowing("geo-1", "Golden Bridge");
        provider
            .Setup(p => p.GetDestinationDetailsAsync("geo-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DestinationDetailsDto(
                "geo-2", "Marble Mountains", null, null, null, null, null, null, null, null));
        var sut = CreateSut(db, Guid.NewGuid(), provider.Object);
        var trip = await sut.CreateTripAsync(new CreateTripRequest("Da Nang"));

        var first = await sut.AddDestinationAsync(trip.Id, new AddDestinationRequest("geo-1", null));
        var second = await sut.AddDestinationAsync(trip.Id, new AddDestinationRequest("geo-2", null));

        Assert.Equal(0, first.SortOrder);
        Assert.Equal(1, second.SortOrder);
    }

    [Fact]
    public async Task RemoveDestinationAsync_RemovesTheItem()
    {
        using var db = CreateDb();
        var sut = CreateSut(db, Guid.NewGuid(), ProviderKnowing("geo-1", "Golden Bridge").Object);
        var trip = await sut.CreateTripAsync(new CreateTripRequest("Da Nang"));
        var item = await sut.AddDestinationAsync(trip.Id, new AddDestinationRequest("geo-1", null));

        await sut.RemoveDestinationAsync(trip.Id, item.ItemId);

        Assert.Equal(0, await db.ItineraryItems.CountAsync());
        Assert.Equal(1, await db.Destinations.CountAsync()); // cache row stays
    }

    [Fact]
    public async Task RemoveDestinationAsync_ItemOnAnotherUsersTrip_ThrowsNotFound()
    {
        using var db = CreateDb();
        var owner = CreateSut(db, Guid.NewGuid(), ProviderKnowing("geo-1", "Golden Bridge").Object);
        var trip = await owner.CreateTripAsync(new CreateTripRequest("Private"));
        var item = await owner.AddDestinationAsync(trip.Id, new AddDestinationRequest("geo-1", null));

        var stranger = CreateSut(db, Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            stranger.RemoveDestinationAsync(trip.Id, item.ItemId));
        Assert.Equal(1, await db.ItineraryItems.CountAsync()); // nothing deleted
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
