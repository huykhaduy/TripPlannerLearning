using TripPlanner.Domain.Common;
using TripPlanner.Domain.Exceptions;

namespace TripPlanner.Domain.Entities;

/// <summary>
/// A trip a user is planning (Feature 3: Trip Planner).
/// A trip owns a set of itinerary days (one per date in its range) and a set of
/// items (destinations). An item with no <see cref="ItineraryItem.ItineraryDayId"/>
/// lives in the "Saved Places" bucket until it is scheduled into a day.
///
/// The trip is the aggregate root for all three: every rule about which days
/// exist, which bucket an item sits in, and what order items appear in is
/// enforced here, so an Application service cannot leave the graph inconsistent.
/// </summary>
public class Trip : BaseEntity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public required string Name { get; set; }

    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }

    public ICollection<ItineraryDay> Days { get; set; } = new List<ItineraryDay>();
    public ICollection<ItineraryItem> Items { get; set; } = new List<ItineraryItem>();

    /// <summary>
    /// Business rule (F3/US2): start date must be on or before the end date.
    ///
    /// Also regenerates the itinerary days for the new range — the two are one
    /// operation, not two, because "Days correspond to the date range" is an
    /// invariant of this aggregate. Callers that could set dates without
    /// regenerating would be able to break it.
    /// </summary>
    public void SetDates(DateOnly? startDate, DateOnly? endDate)
    {
        if (startDate is not null && endDate is not null && startDate > endDate)
        {
            throw new DomainException("Trip start date must be on or before the end date.");
        }

        StartDate = startDate;
        EndDate = endDate;

        RegenerateDays();
    }

    /// <summary>
    /// F3/US2 day regeneration (spec §11.1): keep days still inside the date
    /// range (preserving their scheduled items), delete days that fell out of
    /// it (their items return to Saved Places), create days for new dates,
    /// then renumber chronologically.
    /// </summary>
    private void RegenerateDays()
    {
        var targetDates = new HashSet<DateOnly>();
        if (StartDate is { } start && EndDate is { } end)
        {
            for (var date = start; date <= end; date = date.AddDays(1))
            {
                targetDates.Add(date);
            }
        }

        foreach (var day in Days.Where(d => !targetDates.Contains(d.Date)).ToList())
        {
            // Mirror the DB's SetNull cascade in memory so a DTO built straight
            // afterwards already shows these items back in Saved Places. Driven off
            // Items (the aggregate's own list) rather than day.Items, so it holds
            // even when the day's child collection was never loaded.
            foreach (var item in Items.Where(i => i.ItineraryDayId == day.Id))
            {
                item.ItineraryDayId = null;
            }

            // Days is configured OnDelete(Cascade), so removing the day from the
            // tracked collection marks the row deleted.
            Days.Remove(day);
        }

        var existingDates = Days.Select(d => d.Date).ToHashSet();
        foreach (var date in targetDates.Where(d => !existingDates.Contains(d)))
        {
            Days.Add(new ItineraryDay { TripId = Id, Date = date });
        }

        var dayNumber = 1;
        foreach (var day in Days.OrderBy(d => d.Date))
        {
            day.DayNumber = dayNumber++;
        }
    }

    /// <summary>
    /// The SortOrder a newly added item should take to land at the end of the
    /// given bucket (<paramref name="itineraryDayId"/> null = Saved Places).
    /// </summary>
    public int NextSortOrderIn(Guid? itineraryDayId)
    {
        var bucket = Items.Where(i => i.ItineraryDayId == itineraryDayId).ToList();
        return bucket.Count == 0 ? 0 : bucket.Max(i => i.SortOrder) + 1;
    }

    /// <summary>
    /// Duplicate rule (F3/US4-US6): a destination appears at most once per day, and
    /// at most once in the Saved Places bucket. Pass <paramref name="excludeItemId"/>
    /// when moving an existing item so it cannot conflict with itself.
    ///
    /// Reports the fact; the caller decides what it means. This is deliberately a
    /// predicate rather than a throwing guard — a duplicate is a request-level
    /// conflict (HTTP 409), which is the Application layer's vocabulary, not the
    /// Domain's.
    /// </summary>
    public bool HasDestinationIn(Guid destinationId, Guid? itineraryDayId, Guid? excludeItemId = null) =>
        Items.Any(i => i.Id != excludeItemId
            && i.DestinationId == destinationId
            && i.ItineraryDayId == itineraryDayId);

    /// <summary>
    /// Spec §11.1 US4-US6: move <paramref name="item"/> into
    /// <paramref name="targetDayId"/> (null = Saved Places) at
    /// <paramref name="sortOrder"/>, then renumber both affected buckets 0..n so
    /// values stay dense. The position is clamped, so "99" means last.
    /// </summary>
    public void MoveItem(ItineraryItem item, Guid? targetDayId, int sortOrder)
    {
        var sourceDayId = item.ItineraryDayId;
        item.ItineraryDayId = targetDayId;

        var target = Items
            .Where(i => i.Id != item.Id && i.ItineraryDayId == targetDayId)
            .OrderBy(i => i.SortOrder)
            .ToList();
        target.Insert(Math.Min(sortOrder, target.Count), item);
        Resequence(target);

        // The bucket the item left keeps its relative order but closes the gap.
        if (sourceDayId != targetDayId)
        {
            Resequence(Items
                .Where(i => i.Id != item.Id && i.ItineraryDayId == sourceDayId)
                .OrderBy(i => i.SortOrder)
                .ToList());
        }
    }

    private static void Resequence(List<ItineraryItem> bucket)
    {
        for (var position = 0; position < bucket.Count; position++)
        {
            bucket[position].SortOrder = position;
        }
    }
}
