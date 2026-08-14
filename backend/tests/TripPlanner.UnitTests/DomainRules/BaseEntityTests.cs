using TripPlanner.Domain.Entities;
using Xunit;

namespace TripPlanner.UnitTests.DomainRules;

/// <summary>
/// Entities supply their own keys. That is not a stylistic choice — it is why every
/// entity configuration declares <c>ValueGeneratedNever()</c>, and why TripService
/// can add a day and an item to a loaded trip and finish with one save. See
/// DestinationConfigurationTests for the mapping side of the same decision.
/// </summary>
public class BaseEntityTests
{
    [Fact]
    public void ANewEntityAlreadyHasAnId()
    {
        // Without this, EF would have to generate the key, and TripService could not
        // wire up ItineraryItem.ItineraryDayId before saving.
        var trip = new Trip { UserId = Guid.NewGuid(), Name = "Vietnam" };

        Assert.NotEqual(Guid.Empty, trip.Id);
    }

    [Fact]
    public void EachNewEntityGetsItsOwnId()
    {
        var first = new Destination { ProviderId = "geo-1", Name = "A" };
        var second = new Destination { ProviderId = "geo-2", Name = "B" };

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void ANewEntityIsStampedAsCreatedNow()
    {
        var before = DateTimeOffset.UtcNow;

        var user = new User { Email = "ada@example.com", PasswordHash = "hash" };

        Assert.InRange(user.CreatedAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void ANewEntityHasNotBeenUpdatedYet()
    {
        // UpdatedAt stays null until SaveChangesAsync stamps a modification —
        // ApplicationDbContextTests covers that half.
        var day = new ItineraryDay { TripId = Guid.NewGuid(), Date = new DateOnly(2026, 3, 1), DayNumber = 1 };

        Assert.Null(day.UpdatedAt);
    }

    [Fact]
    public void EveryEntityTypeCarriesTheSameIdentityBehaviour()
    {
        // All five inherit BaseEntity; one that did not would break the
        // ValueGeneratedNever assumption for its own configuration only.
        var entities = new TripPlanner.Domain.Common.BaseEntity[]
        {
            new User { Email = "a@b.c", PasswordHash = "h" },
            new Trip { UserId = Guid.NewGuid(), Name = "t" },
            new ItineraryDay { TripId = Guid.NewGuid(), Date = new DateOnly(2026, 3, 1), DayNumber = 1 },
            new ItineraryItem { TripId = Guid.NewGuid(), DestinationId = Guid.NewGuid() },
            new Destination { ProviderId = "geo-1", Name = "d" },
        };

        Assert.All(entities, e => Assert.NotEqual(Guid.Empty, e.Id));
        Assert.All(entities, e => Assert.Null(e.UpdatedAt));
    }

    [Fact]
    public void AnItemWithNoDayIsASavedPlace()
    {
        // Null ItineraryDayId is the encoding of the Saved Places bucket, relied on
        // by ToDetailDto and by the reorder logic.
        var item = new ItineraryItem { TripId = Guid.NewGuid(), DestinationId = Guid.NewGuid() };

        Assert.Null(item.ItineraryDayId);
        Assert.Equal(0, item.SortOrder);
    }

    [Fact]
    public void ANewUserIsNotVerifiedYet()
    {
        // F4/US2 blocks login until the address is verified, so the default has to
        // be false — a default of true would let unverified accounts straight in.
        var user = new User { Email = "ada@example.com", PasswordHash = "hash" };

        Assert.False(user.IsEmailVerified);
    }
}
