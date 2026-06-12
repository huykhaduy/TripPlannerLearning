using Microsoft.AspNetCore.Mvc;
using TripPlanner.Application.Features.Destinations;
using TripPlanner.Application.Features.Destinations.Dtos;

namespace TripPlanner.WebApi.Controllers;

/// <summary>
/// Destination search &amp; details endpoints (Features 1 &amp; 2). These are PUBLIC
/// (no [Authorize]) because users browse destinations before logging in
/// (F3/US8). The underlying <see cref="IDestinationService"/> is a STUB.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DestinationsController : ControllerBase
{
    private readonly IDestinationService _destinationService;

    public DestinationsController(IDestinationService destinationService)
    {
        _destinationService = destinationService;
    }

    /// <summary>F1/US1-US2 — autocomplete / search for a city or country.</summary>
    [HttpGet("locations")]
    public async Task<ActionResult<IReadOnlyList<LocationSuggestionDto>>> SearchLocations([FromQuery] string query, CancellationToken cancellationToken)
        => Ok(await _destinationService.SearchLocationsAsync(query, cancellationToken));

    /// <summary>F1/US3 — recommended attractions near a coordinate.</summary>
    [HttpGet("attractions")]
    public async Task<ActionResult<IReadOnlyList<DestinationSummaryDto>>> GetAttractions(
        [FromQuery] double lat, [FromQuery] double lng, [FromQuery] double radiusKm = 20, CancellationToken cancellationToken = default)
        => Ok(await _destinationService.GetAttractionsAsync(lat, lng, radiusKm, cancellationToken));

    /// <summary>F2/US1 — full details for a single destination.</summary>
    [HttpGet("{providerId}")]
    public async Task<ActionResult<DestinationDetailsDto>> GetDetails(string providerId, CancellationToken cancellationToken)
        => Ok(await _destinationService.GetDetailsAsync(providerId, cancellationToken));
}
