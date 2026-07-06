using TripPlanner.Domain.Common;

namespace TripPlanner.Domain.Entities;

/// <summary>
/// A place / attraction the user can add to a trip (Features 1 &amp; 2).
/// Data originates from an external provider (e.g. Geoapify);
/// we cache the fields we care about so a trip still renders if the provider
/// is unavailable later.
/// </summary>
public class Destination : BaseEntity
{
    /// <summary>The provider's stable identifier (e.g. Geoapify "place_id").</summary>
    public required string ProviderId { get; set; }

    public required string Name { get; set; }

    public string? Category { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public string? Address { get; set; }
    public string? Website { get; set; }
    public string? OpeningHours { get; set; }
}
