using TripPlanner.Domain.Common;
using TripPlanner.Domain.Exceptions;

namespace TripPlanner.Domain.Entities;

/// <summary>
/// A trip a user is planning (Feature 3: Trip Planner).
/// A trip owns a set of itinerary days (one per date in its range) and a set of
/// items (destinations). An item with no <see cref="ItineraryItem.ItineraryDayId"/>
/// lives in the "Saved Places" bucket until it is scheduled into a day.
/// </summary>
public class Trip : BaseEntity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public required string Name { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public ICollection<ItineraryDay> Days { get; set; } = new List<ItineraryDay>();
    public ICollection<ItineraryItem> Items { get; set; } = new List<ItineraryItem>();

    /// <summary>
    /// Business rule (F3/US2): start date must be on or before the end date.
    /// Call this from the Application layer before persisting date changes.
    /// </summary>
    public void SetDates(DateOnly? startDate, DateOnly? endDate)
    {
        if (startDate is not null && endDate is not null && startDate > endDate)
        {
            throw new DomainException("Trip start date must be on or before the end date.");
        }

        StartDate = startDate;
        EndDate = endDate;
    }
}
