using Microsoft.EntityFrameworkCore;
using TripPlanner.Domain.Entities;
using TripPlanner.Infrastructure.Persistence;
using Xunit;

namespace TripPlanner.UnitTests.Persistence;

/// <summary>
/// Pins a deliberate absence in DestinationConfiguration: ProviderId carries no
/// max length.
///
/// This asserts the model metadata rather than a save, because the InMemory provider
/// does not enforce HasMaxLength at all — an over-long value inserts happily here and
/// only fails against Postgres (22001). So a behavioural test cannot catch a
/// reintroduced cap, and this is the only level at which the decision can be pinned.
///
/// The cap that used to be here was varchar(128), chosen on the assumption that a
/// Geoapify place_id is ~50 characters. It is not: it is a ~68-char prefix plus the
/// hex-encoded UTF-8 place name (2 chars per byte), so real ids run 62–328 characters
/// and roughly one POI in eight blew past 128 — breaking "add to trip" for any place
/// with a long or non-Latin name.
/// </summary>
public class DestinationConfigurationTests
{
    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public void ProviderId_HasNoMaxLength_SoLongPlaceIdsStillFit()
    {
        using var db = CreateDb();

        var providerId = db.Model
            .FindEntityType(typeof(Destination))!
            .FindProperty(nameof(Destination.ProviderId))!;

        Assert.Null(providerId.GetMaxLength());
        Assert.False(providerId.IsNullable);
    }
}
