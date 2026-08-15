using TripPlanner.Domain.Entities;
using TripPlanner.Domain.Exceptions;
using Xunit;

namespace TripPlanner.UnitTests.DomainRules;

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

    // ---- Day regeneration (F3/US2, spec §11.1) -------------------------------
    // Driven by SetDates rather than exposed separately: the invariant is "Days
    // always match the date range", and a caller that could set dates without
    // regenerating would be able to break it.

    [Fact]
    public void SetDates_CreatesOneDayPerDateInTheRange()
    {
        var trip = NewTrip();

        trip.SetDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 3));

        Assert.Equal(
            new[] { new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3) },
            trip.Days.OrderBy(d => d.Date).Select(d => d.Date));
        Assert.Equal(new[] { 1, 2, 3 }, trip.Days.OrderBy(d => d.Date).Select(d => d.DayNumber));
    }

    [Fact]
    public void SetDates_WithAHalfOpenRange_ProducesNoDays()
    {
        // SetDates already allows a start with no end; this is what that means
        // for the itinerary.
        var trip = NewTrip();

        trip.SetDates(new DateOnly(2026, 3, 1), null);

        Assert.Empty(trip.Days);
    }

    [Fact]
    public void SetDates_KeepsDaysStillInRangeAlongWithTheirScheduledItems()
    {
        var trip = NewTrip();
        trip.SetDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 3));
        var secondDay = trip.Days.Single(d => d.Date == new DateOnly(2026, 3, 2));
        var item = new ItineraryItem { TripId = trip.Id, DestinationId = Guid.NewGuid(), ItineraryDayId = secondDay.Id };
        trip.Items.Add(item);
        secondDay.Items.Add(item);

        // Shift the window; 3 Mar drops out, 2 Mar stays.
        trip.SetDates(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 4));

        Assert.Same(secondDay, trip.Days.Single(d => d.Date == new DateOnly(2026, 3, 2)));
        Assert.Equal(secondDay.Id, item.ItineraryDayId);
    }

    [Fact]
    public void SetDates_ReturnsItemsOfDroppedDaysToSavedPlaces()
    {
        // Mirrors the DB's SetNull cascade in memory, so the DTO built straight
        // afterwards already shows these back in Saved Places.
        var trip = NewTrip();
        trip.SetDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 2));
        var doomedDay = trip.Days.Single(d => d.Date == new DateOnly(2026, 3, 2));
        var item = new ItineraryItem { TripId = trip.Id, DestinationId = Guid.NewGuid(), ItineraryDayId = doomedDay.Id };
        trip.Items.Add(item);
        doomedDay.Items.Add(item);

        trip.SetDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1));

        Assert.DoesNotContain(trip.Days, d => d.Date == new DateOnly(2026, 3, 2));
        Assert.Null(item.ItineraryDayId);
        Assert.Contains(item, trip.Items); // still on the trip, just unscheduled
    }

    [Fact]
    public void SetDates_RenumbersDaysChronologicallyAfterTheRangeMoves()
    {
        var trip = NewTrip();
        trip.SetDates(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3));

        // Prepend a day: the surviving days must renumber, not keep their old numbers.
        trip.SetDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 3));

        Assert.Equal(
            new[] { 1, 2, 3 },
            trip.Days.OrderBy(d => d.Date).Select(d => d.DayNumber));
    }

    [Fact]
    public void SetDates_WhenTheRangeIsCleared_DropsEveryDay()
    {
        var trip = NewTrip();
        trip.SetDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 3));

        trip.SetDates(null, null);

        Assert.Empty(trip.Days);
    }

    [Fact]
    public void SetDates_WhenItRejects_LeavesTheDaysUntouched()
    {
        // The date guard runs before anything is regenerated — a rejected change
        // must not leave the itinerary half-rebuilt.
        var trip = NewTrip();
        trip.SetDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 3));

        Assert.Throws<DomainException>(
            () => trip.SetDates(new DateOnly(2026, 4, 10), new DateOnly(2026, 4, 1)));

        Assert.Equal(3, trip.Days.Count);
    }

    // ---- Item placement and ordering (F3/US4-US6, spec §11.1) ----------------

    /// <summary>A trip with one day and <paramref name="savedPlaceCount"/> items in Saved Places.</summary>
    private static Trip TripWithBuckets(int savedPlaceCount)
    {
        var trip = NewTrip();
        trip.SetDates(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1));

        for (var i = 0; i < savedPlaceCount; i++)
        {
            trip.Items.Add(new ItineraryItem
            {
                TripId = trip.Id,
                DestinationId = Guid.NewGuid(),
                ItineraryDayId = null,
                SortOrder = i,
            });
        }

        return trip;
    }

    [Fact]
    public void NextSortOrderIn_AnEmptyBucket_IsZero()
    {
        var trip = TripWithBuckets(savedPlaceCount: 0);

        Assert.Equal(0, trip.NextSortOrderIn(null));
    }

    [Fact]
    public void NextSortOrderIn_APopulatedBucket_IsOnePastTheHighest()
    {
        var trip = TripWithBuckets(savedPlaceCount: 3); // SortOrders 0,1,2

        Assert.Equal(3, trip.NextSortOrderIn(null));
        Assert.Equal(0, trip.NextSortOrderIn(trip.Days.Single().Id)); // the day is empty
    }

    [Fact]
    public void MoveItem_WithinTheSameBucket_ReordersAndKeepsSortOrdersDense()
    {
        var trip = TripWithBuckets(savedPlaceCount: 3);
        var last = trip.Items.Single(i => i.SortOrder == 2);

        trip.MoveItem(last, targetDayId: null, sortOrder: 0);

        Assert.Equal(0, last.SortOrder);
        Assert.Equal(new[] { 0, 1, 2 }, trip.Items.OrderBy(i => i.SortOrder).Select(i => i.SortOrder));
    }

    [Fact]
    public void MoveItem_ToAnotherBucket_ResequencesTheBucketItLeft()
    {
        var trip = TripWithBuckets(savedPlaceCount: 3);
        var dayId = trip.Days.Single().Id;
        var middle = trip.Items.Single(i => i.SortOrder == 1);

        trip.MoveItem(middle, targetDayId: dayId, sortOrder: 0);

        Assert.Equal(dayId, middle.ItineraryDayId);
        Assert.Equal(0, middle.SortOrder);
        // The gap the item left is closed: 0,1 rather than 0,2.
        Assert.Equal(
            new[] { 0, 1 },
            trip.Items.Where(i => i.ItineraryDayId is null).OrderBy(i => i.SortOrder).Select(i => i.SortOrder));
    }

    [Fact]
    public void MoveItem_WithAPositionPastTheEndOfTheBucket_ClampsToLast()
    {
        // TripService relies on this clamp — that is why UpdateItineraryItemRequestValidator
        // deliberately does not reject a large SortOrder.
        var trip = TripWithBuckets(savedPlaceCount: 3);
        var first = trip.Items.Single(i => i.SortOrder == 0);

        trip.MoveItem(first, targetDayId: null, sortOrder: 99);

        Assert.Equal(2, first.SortOrder);
    }

    [Fact]
    public void HasDestinationIn_TheSameBucket_IsTrue()
    {
        var trip = TripWithBuckets(savedPlaceCount: 1);
        var existing = trip.Items.Single();

        Assert.True(trip.HasDestinationIn(existing.DestinationId, itineraryDayId: null));
    }

    [Fact]
    public void HasDestinationIn_ADifferentBucket_IsFalse()
    {
        // The same place may appear once per day AND once in Saved Places.
        var trip = TripWithBuckets(savedPlaceCount: 1);
        var existing = trip.Items.Single();

        Assert.False(trip.HasDestinationIn(existing.DestinationId, trip.Days.Single().Id));
    }

    [Fact]
    public void HasDestinationIn_IgnoringTheItemBeingMoved_IsFalse()
    {
        // Reordering an item within its own bucket must not conflict with itself.
        var trip = TripWithBuckets(savedPlaceCount: 1);
        var existing = trip.Items.Single();

        Assert.False(trip.HasDestinationIn(existing.DestinationId, itineraryDayId: null, excludeItemId: existing.Id));
    }
}
