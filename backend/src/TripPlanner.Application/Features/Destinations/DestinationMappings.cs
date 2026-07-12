using TripPlanner.Application.Features.Destinations.Dtos;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Application.Features.Destinations;

/// <summary>
/// Mapping for the Destinations feature. Provider DTO → cache entity is here
/// so the cache row is shaped identically no matter which use-case creates it.
/// </summary>
public static class DestinationMappings
{
    public static Destination ToEntity(this DestinationDetailsDto details) => new()
    {
        ProviderId = details.ProviderId,
        Name = details.Name,
        Category = details.Category,
        Description = details.Description,
        ImageUrl = details.ImageUrl,
        Latitude = details.Latitude,
        Longitude = details.Longitude,
        Address = details.Address,
        Website = details.Website,
        OpeningHours = details.OpeningHours,
    };

    /// <summary>
    /// F2/US1 fallback path — the cached row survives even when the provider no
    /// longer knows a place, so a saved-trip destination stays viewable.
    /// </summary>
    public static DestinationDetailsDto ToDetailsDto(this Destination destination) => new(
        ProviderId: destination.ProviderId,
        Name: destination.Name,
        Category: destination.Category,
        Description: destination.Description,
        ImageUrl: destination.ImageUrl,
        Latitude: destination.Latitude,
        Longitude: destination.Longitude,
        Address: destination.Address,
        Website: destination.Website,
        OpeningHours: destination.OpeningHours);
}
