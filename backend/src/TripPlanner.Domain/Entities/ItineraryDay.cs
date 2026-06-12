using TripPlanner.Domain.Common;

namespace TripPlanner.Domain.Entities;

/// <summary>
/// A single day within a trip's itinerary (Feature 3: Trip Planner).
/// One day is created for each date between the trip's start and end dates.
/// </summary>
public class ItineraryDay : BaseEntity
{
    public Guid TripId { get; set; }
    public Trip? Trip { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>1-based day number shown in the UI ("Day 1", "Day 2"...).</summary>
    public int DayNumber { get; set; }

    public ICollection<ItineraryItem> Items { get; set; } = new List<ItineraryItem>();
}
