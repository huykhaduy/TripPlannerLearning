using TripPlanner.Application.Features.Trips.Dtos;

namespace TripPlanner.Application.Features.Trips;

/// <summary>
/// Trip planning use-cases (Feature 3), implemented by <see cref="TripService"/>.
/// All methods operate on the CURRENT user only (NFR 6) — read the user id from
/// <see cref="Common.Interfaces.ICurrentUserService"/>.
/// </summary>
public interface ITripService
{
    Task<IReadOnlyList<TripSummaryDto>> GetMyTripsAsync(CancellationToken cancellationToken = default);

    Task<TripDetailDto> GetTripAsync(Guid tripId, CancellationToken cancellationToken = default);

    Task<TripSummaryDto> CreateTripAsync(CreateTripRequest request, CancellationToken cancellationToken = default);

    Task<TripDetailDto> UpdateTripAsync(Guid tripId, UpdateTripRequest request, CancellationToken cancellationToken = default);

    Task<TripDestinationDto> AddDestinationAsync(Guid tripId, AddDestinationRequest request, CancellationToken cancellationToken = default);

    Task<TripDestinationDto> UpdateItineraryItemAsync(Guid tripId, Guid itemId, UpdateItineraryItemRequest request, CancellationToken cancellationToken = default);

    Task RemoveDestinationAsync(Guid tripId, Guid itemId, CancellationToken cancellationToken = default);
}
