using Microsoft.EntityFrameworkCore;
using TripPlanner.Domain.Entities;
using TripPlanner.Infrastructure.Persistence;
using TripPlanner.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TripPlanner.Application.Tests.Persistence;

public class UserRepositoryTests
{
    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    private static User NewUser(string email = "ada@example.com") =>
        new() { Email = email, PasswordHash = "hash", DisplayName = "Ada" };

    [Fact]
    public async Task AddAsync_PersistsImmediately()
    {
        // Every write method saves its own changes — there is no unit of work for a
        // caller to flush afterwards, so an unsaved insert would simply vanish.
        using var db = CreateDb();
        var sut = new UserRepository(db);

        await sut.AddAsync(NewUser());

        // Clearing the tracker first means the row is read back from the store
        // rather than from the identity map, so an unsaved insert cannot pass.
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsTheUser()
    {
        using var db = CreateDb();
        var user = NewUser();
        var sut = new UserRepository(db);
        await sut.AddAsync(user);

        var found = await sut.GetByIdAsync(user.Id);

        Assert.Equal(user.Id, found!.Id);
    }

    [Fact]
    public async Task GetByIdAsync_WithAnUnknownId_ReturnsNull()
    {
        using var db = CreateDb();

        Assert.Null(await new UserRepository(db).GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetByEmailAsync_ReturnsTheUser()
    {
        using var db = CreateDb();
        var sut = new UserRepository(db);
        await sut.AddAsync(NewUser());

        var found = await sut.GetByEmailAsync("ada@example.com");

        Assert.Equal("ada@example.com", found!.Email);
    }

    [Fact]
    public async Task GetByEmailAsync_MatchesExactly()
    {
        // Case folding is AuthService's job (it normalizes before storing and before
        // looking up). Doing it here as well would put the rule in two places, and
        // silently diverge from the unique index, which is on the stored value.
        using var db = CreateDb();
        var sut = new UserRepository(db);
        await sut.AddAsync(NewUser("ada@example.com"));

        Assert.Null(await sut.GetByEmailAsync("ADA@example.com"));
        Assert.NotNull(await sut.GetByEmailAsync("ada@example.com"));
    }

    [Fact]
    public async Task ExistsByEmailAsync_ReportsBothWays()
    {
        using var db = CreateDb();
        var sut = new UserRepository(db);
        await sut.AddAsync(NewUser("ada@example.com"));

        Assert.True(await sut.ExistsByEmailAsync("ada@example.com"));
        Assert.False(await sut.ExistsByEmailAsync("grace@example.com"));
    }

    [Fact]
    public async Task UpdateAsync_PersistsAChangeToTheTrackedUser()
    {
        // The parameter is not used — the entity is already tracked and the call
        // just flushes. Verifying an actual field change is what proves the entity
        // was still tracked rather than that the method quietly did nothing.
        using var db = CreateDb();
        var sut = new UserRepository(db);
        var user = NewUser();
        await sut.AddAsync(user);

        var loaded = await sut.GetByEmailAsync("ada@example.com");
        loaded!.IsEmailVerified = true;
        await sut.UpdateAsync(loaded);

        db.ChangeTracker.Clear();
        Assert.True((await sut.GetByIdAsync(user.Id))!.IsEmailVerified);
    }

    [Fact]
    public async Task GetByIdAsync_TracksTheUserSoAFollowUpUpdateSaves()
    {
        using var db = CreateDb();
        var sut = new UserRepository(db);
        var user = NewUser();
        await sut.AddAsync(user);
        db.ChangeTracker.Clear();

        var loaded = await sut.GetByIdAsync(user.Id);

        // Not AsNoTracking, unlike the read-only trip queries: the verify-email flow
        // loads a user and then flips IsEmailVerified on it.
        Assert.Equal(EntityState.Unchanged, db.Entry(loaded!).State);
    }
}
