using Microsoft.EntityFrameworkCore;
using TripPlanner.Domain.Entities;
using TripPlanner.Infrastructure.Persistence;
using TripPlanner.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TripPlanner.Application.Tests.Repositories;

/// <summary>
/// Unit tests for the generic Repository&lt;T&gt; + UnitOfWork pair, using
/// Destination as a stand-in entity (any BaseEntity would do). Entity-specific
/// repositories are tested alongside the services that use them (Tasks 2-4).
/// </summary>
public class RepositoryTests
{
    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task Add_ThenSaveChanges_PersistsTheEntity()
    {
        using var db = CreateDb();
        var repository = new Repository<Destination>(db);
        var unitOfWork = new UnitOfWork(db);

        repository.Add(new Destination { ProviderId = "geo-1", Name = "Golden Bridge" });
        await unitOfWork.SaveChangesAsync();

        Assert.Equal(1, await db.Destinations.CountAsync());
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsThePersistedEntity()
    {
        using var db = CreateDb();
        var repository = new Repository<Destination>(db);
        var unitOfWork = new UnitOfWork(db);
        var destination = new Destination { ProviderId = "geo-1", Name = "Golden Bridge" };
        repository.Add(destination);
        await unitOfWork.SaveChangesAsync();

        var found = await repository.GetByIdAsync(destination.Id);

        Assert.NotNull(found);
        Assert.Equal("Golden Bridge", found!.Name);
    }

    [Fact]
    public async Task GetByIdAsync_WithUnknownId_ReturnsNull()
    {
        using var db = CreateDb();
        var repository = new Repository<Destination>(db);

        Assert.Null(await repository.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEveryPersistedEntity()
    {
        using var db = CreateDb();
        var repository = new Repository<Destination>(db);
        var unitOfWork = new UnitOfWork(db);
        repository.Add(new Destination { ProviderId = "geo-1", Name = "Golden Bridge" });
        repository.Add(new Destination { ProviderId = "geo-2", Name = "Marble Mountains" });
        await unitOfWork.SaveChangesAsync();

        Assert.Equal(2, (await repository.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Remove_ThenSaveChanges_DeletesTheEntity()
    {
        using var db = CreateDb();
        var repository = new Repository<Destination>(db);
        var unitOfWork = new UnitOfWork(db);
        var destination = new Destination { ProviderId = "geo-1", Name = "Golden Bridge" };
        repository.Add(destination);
        await unitOfWork.SaveChangesAsync();

        repository.Remove(destination);
        await unitOfWork.SaveChangesAsync();

        Assert.Equal(0, await db.Destinations.CountAsync());
    }
}
