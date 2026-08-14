using TripPlanner.Domain.Entities;
using TripPlanner.Domain.Exceptions;
using Xunit;

namespace TripPlanner.Application.Tests.DomainRules;

/// <summary>
/// SetDates is the only business rule that lives in the Domain rather than in a
/// validator, and it is deliberately not duplicated in UpdateTripRequestValidator
/// (there is a validator test pinning that absence). These tests are what keeps the
/// rule real — the service layer just calls this and lets DomainException become a
/// 400 on the way out.
/// </summary>
public class TripTests
{
    private static Trip NewTrip() => new() { UserId = Guid.NewGuid(), Name = "Vietnam 2026" };

    [Fact]
    public void SetDates_WithStartBeforeEnd_Sets()
    {
        var trip = NewTrip();

        trip.SetDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 8));

        Assert.Equal(new DateOnly(2026, 3, 1), trip.StartDate);
        Assert.Equal(new DateOnly(2026, 3, 8), trip.EndDate);
    }

    [Fact]
    public void SetDates_WithTheSameDayForBoth_IsAllowed()
    {
        // The rule is "on or before", not "before" — a one-day trip is legitimate.
        var trip = NewTrip();
        var sameDay = new DateOnly(2026, 3, 1);

        trip.SetDates(sameDay, sameDay);

        Assert.Equal(sameDay, trip.StartDate);
        Assert.Equal(sameDay, trip.EndDate);
    }

    [Fact]
    public void SetDates_WithEndBeforeStart_Throws()
    {
        var trip = NewTrip();

        var ex = Assert.Throws<DomainException>(
            () => trip.SetDates(new DateOnly(2026, 3, 8), new DateOnly(2026, 3, 1)));

        Assert.Contains("on or before", ex.Message);
    }

    [Fact]
    public void SetDates_WithEndBeforeStartByOneDay_Throws()
    {
        // The boundary either side: the same day passes, one day earlier does not.
        var trip = NewTrip();

        Assert.Throws<DomainException>(
            () => trip.SetDates(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 1)));
    }

    [Fact]
    public void SetDates_WhenItRejects_LeavesTheExistingDatesUntouched()
    {
        // The guard runs before either assignment. Assigning first and validating
        // after would leave a trip half-updated once the exception unwound.
        var trip = NewTrip();
        trip.SetDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 8));

        Assert.Throws<DomainException>(
            () => trip.SetDates(new DateOnly(2027, 1, 10), new DateOnly(2027, 1, 1)));

        Assert.Equal(new DateOnly(2026, 3, 1), trip.StartDate);
        Assert.Equal(new DateOnly(2026, 3, 8), trip.EndDate);
    }

    [Fact]
    public void SetDates_WithBothNull_ClearsTheRange()
    {
        // Clearing the dates is how a user drops the itinerary back to Saved Places,
        // so it must not be caught by the comparison.
        var trip = NewTrip();
        trip.SetDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 8));

        trip.SetDates(null, null);

        Assert.Null(trip.StartDate);
        Assert.Null(trip.EndDate);
    }

    [Fact]
    public void SetDates_WithOnlyAStart_IsAllowed()
    {
        // A half-open range cannot violate "start before end", so it is accepted;
        // the day-generation logic is what decides it produces no days.
        var trip = NewTrip();

        trip.SetDates(new DateOnly(2026, 3, 1), null);

        Assert.Equal(new DateOnly(2026, 3, 1), trip.StartDate);
        Assert.Null(trip.EndDate);
    }

    [Fact]
    public void SetDates_WithOnlyAnEnd_IsAllowed()
    {
        var trip = NewTrip();

        trip.SetDates(null, new DateOnly(2026, 3, 8));

        Assert.Null(trip.StartDate);
        Assert.Equal(new DateOnly(2026, 3, 8), trip.EndDate);
    }

    [Fact]
    public void ATripStartsWithEmptyDaysAndItems()
    {
        // Initialised collections, not null — TripService adds straight into them
        // without a null check.
        var trip = NewTrip();

        Assert.Empty(trip.Days);
        Assert.Empty(trip.Items);
    }
}
