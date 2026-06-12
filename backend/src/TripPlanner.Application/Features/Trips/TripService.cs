using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Features.Trips.Dtos;

namespace TripPlanner.Application.Features.Trips;

/// <summary>
/// STUB — students implement this (Feature 3: Trip Planner).
///
/// Use <see cref="AuthService"/> as your reference for structure. Key points:
///   * read the owner from <see cref="ICurrentUserService.UserId"/> and filter
///     every query by it — never trust a trip id alone (NFR 6 / authorization);
///   * throw <see cref="Common.Exceptions.NotFoundException"/> when a trip the
///     current user owns does not exist;
///   * when dates change (US2) regenerate <c>ItineraryDay</c> rows for the new
///     range and call <c>Trip.SetDates</c> to enforce start ≤ end.
/// </summary>
public class TripService : ITripService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public TripService(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public Task<IReadOnlyList<TripSummaryDto>> GetMyTripsAsync(CancellationToken cancellationToken = default)
        // TODO: F3/US10 — return trips owned by _currentUser.UserId.
        => throw new NotImplementedException("Implement GetMyTrips — see Feature 3, US10.");

    public Task<TripDetailDto> GetTripAsync(Guid tripId, CancellationToken cancellationToken = default)
        // TODO: F3/US10 — load the trip (owned by current user) with days + items.
        => throw new NotImplementedException("Implement GetTrip — see Feature 3, US10.");

    public Task<TripSummaryDto> CreateTripAsync(CreateTripRequest request, CancellationToken cancellationToken = default)
        // TODO: F3/US1 — validate name, create a Trip for the current user, save.
        => throw new NotImplementedException("Implement CreateTrip — see Feature 3, US1.");

    public Task<TripDetailDto> UpdateTripAsync(Guid tripId, UpdateTripRequest request, CancellationToken cancellationToken = default)
        // TODO: F3/US2 — rename and/or set dates; regenerate itinerary days.
        => throw new NotImplementedException("Implement UpdateTrip — see Feature 3, US2.");

    public Task<TripDestinationDto> AddDestinationAsync(Guid tripId, AddDestinationRequest request, CancellationToken cancellationToken = default)
        // TODO: F3/US3 — add a destination to the trip (optionally into a day).
        => throw new NotImplementedException("Implement AddDestination — see Feature 3, US3.");

    public Task RemoveDestinationAsync(Guid tripId, Guid itemId, CancellationToken cancellationToken = default)
        // TODO: F3/US7 — remove a destination from the trip.
        => throw new NotImplementedException("Implement RemoveDestination — see Feature 3, US7.");
}
