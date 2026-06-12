using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TripPlanner.Application.Features.Trips;
using TripPlanner.Application.Features.Trips.Dtos;

namespace TripPlanner.WebApi.Controllers;

/// <summary>
/// Trip planner endpoints (Feature 3). The routes are wired for you and the
/// whole controller requires authentication ([Authorize]) — but the underlying
/// <see cref="ITripService"/> is a STUB. Implement the service, then these
/// endpoints come alive. Add/adjust endpoints as you build out US4–US9.
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

    [HttpDelete("{tripId:guid}/destinations/{itemId:guid}")]
    public async Task<IActionResult> RemoveDestination(Guid tripId, Guid itemId, CancellationToken cancellationToken)
    {
        await _tripService.RemoveDestinationAsync(tripId, itemId, cancellationToken);
        return NoContent();
    }
}
