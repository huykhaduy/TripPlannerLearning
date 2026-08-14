using Microsoft.EntityFrameworkCore;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Domain.Entities;
using TripPlanner.Infrastructure.Persistence;
using TripPlanner.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TripPlanner.UnitTests.Persistence;

public class DestinationRepositoryTests
{
    private static DbContextOptions<ApplicationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

    private static ApplicationDbContext CreateDb() => new(NewOptions());

    private static Destination NewDestination(string providerId = "geo-1") =>
        new() { ProviderId = providerId, Name = "Imperial Citadel" };

    /// <summary>
    /// A context whose save always loses the race. The InMemory provider does not
    /// enforce unique indexes, so the real translation in ApplicationDbContext is
    /// unreachable here — simulating ConcurrencyException at this seam is the only
    /// way to reach the repository's detach-and-rethrow branch.
    /// </summary>
    private sealed class LosingRaceDbContext(DbContextOptions<ApplicationDbContext> options)
        : ApplicationDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new ConcurrencyException(
                "A concurrent write conflicted with this save.",
                new InvalidOperationException("simulated unique violation"));
    }

    [Fact]
    public async Task AddAsync_PersistsImmediately()
    {
        using var db = CreateDb();
        var sut = new DestinationRepository(db);

        await sut.AddAsync(NewDestination());

        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Destinations.CountAsync());
    }

    [Fact]
    public async Task GetByProviderIdAsync_FindsTheCachedRow()
    {
        using var db = CreateDb();
        var sut = new DestinationRepository(db);
        await sut.AddAsync(NewDestination("geo-1"));

        var found = await sut.GetByProviderIdAsync("geo-1");

        Assert.Equal("Imperial Citadel", found!.Name);
    }

    [Fact]
    public async Task GetByProviderIdAsync_WithAnUnknownId_ReturnsNull()
    {
        using var db = CreateDb();

        Assert.Null(await new DestinationRepository(db).GetByProviderIdAsync("never-seen"));
    }

    [Fact]
    public async Task GetByProviderIdAsync_TracksTheRow()
    {
        // TripService reuses this row when adding to a trip, so it has to be tracked.
        using var db = CreateDb();
        var sut = new DestinationRepository(db);
        await sut.AddAsync(NewDestination());
        db.ChangeTracker.Clear();

        var found = await sut.GetByProviderIdAsync("geo-1");

        Assert.Equal(EntityState.Unchanged, db.Entry(found!).State);
    }

    [Fact]
    public async Task GetByProviderIdReadOnlyAsync_DoesNotTrackTheRow()
    {
        // The read-only overload exists for the details path, which never writes.
        // Tracking there would put the row in the change tracker of a request that
        // has no intention of saving it.
        using var db = CreateDb();
        var sut = new DestinationRepository(db);
        await sut.AddAsync(NewDestination());
        db.ChangeTracker.Clear();

        var found = await sut.GetByProviderIdReadOnlyAsync("geo-1");

        Assert.Equal(EntityState.Detached, db.Entry(found!).State);
    }

    [Fact]
    public async Task ItHandlesTheLongProviderIdsGeoapifyActuallyReturns()
    {
        // ProviderId is unbounded text by design: a place_id is a ~68-character
        // prefix plus the hex-encoded place name, so real ids run past 300
        // characters. A HasMaxLength here used to make add-to-trip 500.
        var providerId = "51" + new string('a', 320);
        using var db = CreateDb();
        var sut = new DestinationRepository(db);

        await sut.AddAsync(NewDestination(providerId));

        Assert.NotNull(await sut.GetByProviderIdAsync(providerId));
    }

    [Fact]
    public async Task AddAsync_WhenAConcurrentWriteWins_DetachesTheLosingCopy()
    {
        // The caller catches ConcurrencyException and re-fetches the winner. If our
        // losing copy stayed Added on the change tracker, the very next save on that
        // context would retry the same doomed insert.
        var options = NewOptions();
        using var db = new LosingRaceDbContext(options);
        var sut = new DestinationRepository(db);
        var losing = NewDestination();

        await Assert.ThrowsAsync<ConcurrencyException>(() => sut.AddAsync(losing));

        Assert.Equal(EntityState.Detached, db.Entry(losing).State);
    }

    [Fact]
    public async Task AddAsync_WhenAConcurrentWriteWins_StillRethrows()
    {
        // Detaching quietly and returning would hand the caller a destination that
        // was never saved, and TripService would then attach an itinerary item to it.
        var options = NewOptions();
        using var db = new LosingRaceDbContext(options);

        await Assert.ThrowsAsync<ConcurrencyException>(
            () => new DestinationRepository(db).AddAsync(NewDestination()));
    }
}
