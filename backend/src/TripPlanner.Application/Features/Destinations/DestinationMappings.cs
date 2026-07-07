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
}
