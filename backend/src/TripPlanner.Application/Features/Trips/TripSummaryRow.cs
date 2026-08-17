using TripPlanner.Application.Features.Trips.Dtos;

namespace TripPlanner.Application.Features.Trips;

/// <summary>
/// Query row for the trip list: the summary DTO plus <c>CreatedAt</c>, which is
/// needed only for ordering — the list query fetches this row and sorts in
/// memory instead of via SQL ORDER BY.
/// </summary>
public sealed record TripSummaryRow(DateTimeOffset CreatedAt, TripSummaryDto Summary);
