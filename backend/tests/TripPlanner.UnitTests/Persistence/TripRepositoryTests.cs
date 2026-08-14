using Microsoft.EntityFrameworkCore;
using TripPlanner.Domain.Entities;
using TripPlanner.Infrastructure.Persistence;
using TripPlanner.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TripPlanner.UnitTests.Persistence;

/// <summary>
/// The ownership filter is the thing to watch here. Every read takes a userId and
/// ANDs it into the predicate, which is what makes another user's trip come back as
/// null — and therefore surface as 404 rather than 403, so an attacker cannot probe
/// ids to find out which ones exist.
/// </summary>
public class TripRepositoryTests
{
    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    /// <summary>A trip with one day, one scheduled item and one saved place.</summary>
    private static async Task<Trip> SeedTripAsync(ApplicationDbContext db, Guid userId)
    {
        var scheduled = new Destination { ProviderId = "geo-1", Name = "Imperial Citadel", ImageUrl = "https://img.test/a.jpg" };
        var saved = new Destination { ProviderId = "geo-2", Name = "Hoan Kiem Lake" };
        db.Destinations.AddRange(scheduled, saved);

        var trip = new Trip { UserId = userId, Name = "Vietnam 2026" };
        var day = new ItineraryDay { TripId = trip.Id, Date = new DateOnly(2026, 3, 1), DayNumber = 1 };
        trip.Days.Add(day);
        trip.Items.Add(new ItineraryItem
        {
            TripId = trip.Id,
            DestinationId = scheduled.Id,
            ItineraryDayId = day.Id,
            SortOrder = 0,
        });
        trip.Items.Add(new ItineraryItem { TripId = trip.Id, DestinationId = saved.Id, SortOrder = 0 });

        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return trip;
    }

    // ------------------------------------------------------------------
    // Summary rows
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetSummaryRowsForUserAsync_ReturnsOnlyThatUsersTrips()
    {
        using var db = CreateDb();
        await SeedTripAsync(db, Owner);
        await SeedTripAsync(db, Stranger);
        var sut = new TripRepository(db);

        var rows = await sut.GetSummaryRowsForUserAsync(Owner);

        Assert.Equal("Vietnam 2026", Assert.Single(rows).Summary.Name);
    }

    [Fact]
    public async Task GetSummaryRowsForUserAsync_CountsEveryDestinationIncludingSavedPlaces()
    {
        using var db = CreateDb();
        await SeedTripAsync(db, Owner);

        var row = Assert.Single(await new TripRepository(db).GetSummaryRowsForUserAsync(Owner));

        // Items, not Days.Items — an unscheduled place is still in the trip.
        Assert.Equal(2, row.Summary.DestinationCount);
    }

    [Fact]
    public async Task GetSummaryRowsForUserAsync_PicksACoverPhotoFromTheFirstDestinationThatHasOne()
    {
        using var db = CreateDb();
        await SeedTripAsync(db, Owner);

        var row = Assert.Single(await new TripRepository(db).GetSummaryRowsForUserAsync(Owner));

        Assert.Equal("https://img.test/a.jpg", row.Summary.CoverImageUrl);
    }

    [Fact]
    public async Task GetSummaryRowsForUserAsync_WithNoTrips_ReturnsEmpty()
    {
        using var db = CreateDb();

        Assert.Empty(await new TripRepository(db).GetSummaryRowsForUserAsync(Owner));
    }

    [Fact]
    public async Task GetSummaryRowsForUserAsync_CarriesCreatedAtForOrdering()
    {
        // The row exists only because the list sorts on CreatedAt in memory; drop it
        // from the projection and the sort silently becomes insertion order.
        using var db = CreateDb();
        await SeedTripAsync(db, Owner);

        var row = Assert.Single(await new TripRepository(db).GetSummaryRowsForUserAsync(Owner));

        Assert.NotEqual(default, row.CreatedAt);
    }

    // ------------------------------------------------------------------
    // Details
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetDetailsAsync_LoadsTheWholeGraph()
    {
        // ToDetailDto walks Days → Items → Destination and Items → Destination; a
        // missing Include surfaces as a NullReferenceException at mapping time.
        using var db = CreateDb();
        var trip = await SeedTripAsync(db, Owner);

        var loaded = await new TripRepository(db).GetDetailsAsync(trip.Id, Owner);

        Assert.NotNull(loaded);
        var day = Assert.Single(loaded.Days);
        Assert.Equal("Imperial Citadel", Assert.Single(day.Items).Destination!.Name);
        Assert.Equal(2, loaded.Items.Count);
        Assert.All(loaded.Items, i => Assert.NotNull(i.Destination));
    }

    [Fact]
    public async Task GetDetailsAsync_ForAnotherUsersTrip_ReturnsNull()
    {
        using var db = CreateDb();
        var trip = await SeedTripAsync(db, Owner);

        Assert.Null(await new TripRepository(db).GetDetailsAsync(trip.Id, Stranger));
    }

    [Fact]
    public async Task GetDetailsAsync_ForAnUnknownTrip_ReturnsNull()
    {
        // Identical to the previous case on purpose: "not yours" and "does not
        // exist" have to be indistinguishable from the outside.
        using var db = CreateDb();

        Assert.Null(await new TripRepository(db).GetDetailsAsync(Guid.NewGuid(), Owner));
    }

    [Fact]
    public async Task GetDetailsAsync_DoesNotTrackWhatItLoads()
    {
        using var db = CreateDb();
        var trip = await SeedTripAsync(db, Owner);

        var loaded = await new TripRepository(db).GetDetailsAsync(trip.Id, Owner);

        Assert.Equal(EntityState.Detached, db.Entry(loaded!).State);
    }

    // ------------------------------------------------------------------
    // Loading for mutation
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetForUpdateAsync_TracksTheGraphSoOneUpdateSavesItAll()
    {
        // This is what lets TripService just do trip.Days.Add(...) / trip.Items.Add(...)
        // and finish with a single UpdateAsync.
        using var db = CreateDb();
        var trip = await SeedTripAsync(db, Owner);
        var sut = new TripRepository(db);

        var loaded = await sut.GetForUpdateAsync(trip.Id, Owner);
        loaded!.Name = "Vietnam 2027";
        loaded.Days.Add(new ItineraryDay { TripId = trip.Id, Date = new DateOnly(2026, 3, 2), DayNumber = 2 });
        await sut.UpdateAsync(loaded);

        db.ChangeTracker.Clear();
        var reloaded = await sut.GetDetailsAsync(trip.Id, Owner);
        Assert.Equal("Vietnam 2027", reloaded!.Name);
        Assert.Equal(2, reloaded.Days.Count);
    }

    [Fact]
    public async Task GetForUpdateAsync_ForAnotherUsersTrip_ReturnsNull()
    {
        using var db = CreateDb();
        var trip = await SeedTripAsync(db, Owner);

        Assert.Null(await new TripRepository(db).GetForUpdateAsync(trip.Id, Stranger));
    }

    [Fact]
    public async Task AddAsync_PersistsTheTripImmediately()
    {
        using var db = CreateDb();
        var sut = new TripRepository(db);

        await sut.AddAsync(new Trip { UserId = Owner, Name = "New trip" });

        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Trips.CountAsync());
    }

    // ------------------------------------------------------------------
    // Day and item lookups
    // ------------------------------------------------------------------

    [Fact]
    public async Task DayBelongsToTripAsync_TellsTheTwoCasesApart()
    {
        // Checked before scheduling into a day: without it, a user could move an
        // item onto a day belonging to somebody else's trip.
        using var db = CreateDb();
        var trip = await SeedTripAsync(db, Owner);
        var otherTrip = await SeedTripAsync(db, Owner);
        var day = trip.Days.First();
        var sut = new TripRepository(db);

        Assert.True(await sut.DayBelongsToTripAsync(day.Id, trip.Id));
        Assert.False(await sut.DayBelongsToTripAsync(day.Id, otherTrip.Id));
        Assert.False(await sut.DayBelongsToTripAsync(Guid.NewGuid(), trip.Id));
    }

    [Fact]
    public async Task GetOwnedItemAsync_ReturnsTheItem()
    {
        using var db = CreateDb();
        var trip = await SeedTripAsync(db, Owner);
        var item = trip.Items.First();

        var found = await new TripRepository(db).GetOwnedItemAsync(trip.Id, item.Id, Owner);

        Assert.Equal(item.Id, found!.Id);
    }

    [Fact]
    public async Task GetOwnedItemAsync_ForAnotherUser_ReturnsNull()
    {
        // The ownership check reaches through the item's Trip navigation; an item id
        // on its own is never enough to act on a row.
        using var db = CreateDb();
        var trip = await SeedTripAsync(db, Owner);
        var item = trip.Items.First();

        Assert.Null(await new TripRepository(db).GetOwnedItemAsync(trip.Id, item.Id, Stranger));
    }

    [Fact]
    public async Task GetOwnedItemAsync_WithAMismatchedTripId_ReturnsNull()
    {
        using var db = CreateDb();
        var trip = await SeedTripAsync(db, Owner);
        var otherTrip = await SeedTripAsync(db, Owner);
        var item = trip.Items.First();

        Assert.Null(await new TripRepository(db).GetOwnedItemAsync(otherTrip.Id, item.Id, Owner));
    }

    [Fact]
    public async Task RemoveItemAsync_DeletesJustThatItem()
    {
        using var db = CreateDb();
        var trip = await SeedTripAsync(db, Owner);
        var sut = new TripRepository(db);
        var item = (await sut.GetForUpdateAsync(trip.Id, Owner))!.Items.First();

        await sut.RemoveItemAsync(item);

        db.ChangeTracker.Clear();
        var reloaded = await sut.GetDetailsAsync(trip.Id, Owner);
        Assert.Single(reloaded!.Items);
        // Removing an item must not take the destination row with it — it is a
        // shared cache of provider data that other trips may be pointing at.
        Assert.Equal(2, await db.Destinations.CountAsync());
    }
}
