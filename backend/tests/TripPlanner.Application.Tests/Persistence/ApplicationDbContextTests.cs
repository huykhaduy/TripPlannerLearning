using Microsoft.EntityFrameworkCore;
using TripPlanner.Domain.Entities;
using TripPlanner.Infrastructure.Persistence;
using Xunit;

namespace TripPlanner.Application.Tests.Persistence;

/// <summary>
/// Covers the SaveChangesAsync override: audit stamping, and the fact that an
/// ordinary save is left alone. The unique-violation translation it also performs
/// is not reachable here — the InMemory provider does not enforce unique indexes.
/// </summary>
public class ApplicationDbContextTests
{
    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task SaveChangesAsync_OnInsert_LeavesUpdatedAtNull()
    {
        using var db = CreateDb();

        db.Destinations.Add(new Destination { ProviderId = "geo-1", Name = "Golden Bridge" });
        await db.SaveChangesAsync();

        var saved = await db.Destinations.SingleAsync();
        Assert.Null(saved.UpdatedAt);
    }

    [Fact]
    public async Task SaveChangesAsync_OnModify_StampsUpdatedAt()
    {
        using var db = CreateDb();
        var destination = new Destination { ProviderId = "geo-1", Name = "Golden Bridge" };
        db.Destinations.Add(destination);
        await db.SaveChangesAsync();

        destination.Name = "Golden Bridge (renamed)";
        await db.SaveChangesAsync();

        Assert.NotNull(destination.UpdatedAt);
    }
}
