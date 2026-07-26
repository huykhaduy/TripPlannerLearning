using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TripPlanner.Application.Features.Trips;
using TripPlanner.Application.Features.Trips.Dtos;

namespace TripPlanner.WebApi.Controllers;

/// <summary>
/// Trip planner endpoints (Feature 3). The whole controller requires
/// authentication (<see cref="AuthorizeAttribute"/>); every route delegates to
/// <see cref="ITripService"/>, which enforces per-user ownership (NFR 6).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TripsController : ControllerBase
{
    private readonly ITripService _tripService;

    public TripsController(ITripService tripService)
    {
        _tripService = tripService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TripSummaryDto>>> GetMyTrips(CancellationToken cancellationToken)
        => Ok(await _tripService.GetMyTripsAsync(cancellationToken));

    [HttpGet("{tripId:guid}")]
    public async Task<ActionResult<TripDetailDto>> GetTrip(Guid tripId, CancellationToken cancellationToken)
        => Ok(await _tripService.GetTripAsync(tripId, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<TripSummaryDto>> CreateTrip(CreateTripRequest request, CancellationToken cancellationToken)
    {
        var trip = await _tripService.CreateTripAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetTrip), new { tripId = trip.Id }, trip);
    }

    [HttpPut("{tripId:guid}")]
    public async Task<ActionResult<TripDetailDto>> UpdateTrip(Guid tripId, UpdateTripRequest request, CancellationToken cancellationToken)
        => Ok(await _tripService.UpdateTripAsync(tripId, request, cancellationToken));

    [HttpPost("{tripId:guid}/destinations")]
    public async Task<ActionResult<TripDestinationDto>> AddDestination(Guid tripId, AddDestinationRequest request, CancellationToken cancellationToken)
        => Ok(await _tripService.AddDestinationAsync(tripId, request, cancellationToken));

    /// <summary>F3/US4-US6 — schedule, reorder or move an item (null day = Saved Places).</summary>
    [HttpPut("{tripId:guid}/destinations/{itemId:guid}")]
    public async Task<ActionResult<TripDestinationDto>> UpdateItineraryItem(Guid tripId, Guid itemId, UpdateItineraryItemRequest request, CancellationToken cancellationToken)
        => Ok(await _tripService.UpdateItineraryItemAsync(tripId, itemId, request, cancellationToken));

    [HttpDelete("{tripId:guid}/destinations/{itemId:guid}")]
    public async Task<IActionResult> RemoveDestination(Guid tripId, Guid itemId, CancellationToken cancellationToken)
    {
        await _tripService.RemoveDestinationAsync(tripId, itemId, cancellationToken);
        return NoContent();
    }
}
