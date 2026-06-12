using TripPlanner.Domain.Common;

namespace TripPlanner.Domain.Entities;

/// <summary>
/// Links a <see cref="Destination"/> to a <see cref="Trip"/> (Feature 3).
///
/// * When <see cref="ItineraryDayId"/> is <c>null</c> the destination sits in the
///   trip's "Saved Places" bucket (added but not yet scheduled).
/// * When set, the destination is scheduled into that specific day.
///
/// <see cref="SortOrder"/> controls the visit sequence within the bucket/day
/// (Feature 3 / US5 — reorder destinations within a day).
/// </summary>
public class ItineraryItem : BaseEntity
{
    public Guid TripId { get; set; }
    public Trip? Trip { get; set; }

    public Guid DestinationId { get; set; }
    public Destination? Destination { get; set; }

    public Guid? ItineraryDayId { get; set; }
    public ItineraryDay? ItineraryDay { get; set; }

    public int SortOrder { get; set; }
}
