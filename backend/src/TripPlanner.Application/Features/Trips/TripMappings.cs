using System.Linq.Expressions;
using TripPlanner.Application.Features.Trips.Dtos;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Application.Features.Trips;

/// <summary>
/// Entity → DTO mapping for the Trips feature — the single source of truth for
/// what a trip looks like on the wire. Two forms of the same mapping:
///   * <see cref="ToSummaryRowExpression"/> for EF queries (translated to SQL,
///     so Items.Count becomes COUNT(*) instead of loading rows);
///   * <see cref="ToSummaryDto"/> for entities already in memory.
/// The extension is compiled from the expression, so the mapping is defined
/// exactly once — they can never drift apart.
/// </summary>
public static class TripMappings
{
    public static readonly Expression<Func<Trip, TripSummaryRow>> ToSummaryRowExpression =
        trip => new TripSummaryRow(
            trip.CreatedAt,
            new TripSummaryDto(
                trip.Id, trip.Name, trip.StartDate, trip.EndDate, trip.Items.Count,
                // Cover photo: the first (by SortOrder) destination in the trip that
                // actually has an image — translates to a correlated subquery, so
                // the list stays one round trip instead of loading every item.
                trip.Items
                    .Where(i => i.Destination!.ImageUrl != null)
                    .OrderBy(i => i.SortOrder)
                    .Select(i => i.Destination!.ImageUrl)
                    .FirstOrDefault()));

    private static readonly Func<Trip, TripSummaryRow> ToSummaryRowCompiled =
        ToSummaryRowExpression.Compile();

    /// <summary>
    /// Requires <c>trip.Items</c> to be loaded (or the trip to be freshly
    /// created); otherwise the destination count would silently read 0.
    /// </summary>
    public static TripSummaryDto ToSummaryDto(this Trip trip) => ToSummaryRowCompiled(trip).Summary;

    /// <summary>Requires <c>item.Destination</c> to be loaded.</summary>
    public static TripDestinationDto ToDestinationDto(this ItineraryItem item) =>
        new(item.Id, item.Destination!.ProviderId, item.Destination.Name,
            item.Destination.ImageUrl, item.SortOrder);

    public static ItineraryDayDto ToDayDto(this ItineraryDay day) =>
        new(day.Id, day.Date, day.DayNumber,
            day.Items.OrderBy(i => i.SortOrder).Select(ToDestinationDto).ToList());

    /// <summary>
    /// Requires the full graph to be loaded: Days, Items and each item's
    /// Destination. Items without a day form the "Saved Places" bucket.
    /// </summary>
    public static TripDetailDto ToDetailDto(this Trip trip) =>
        new(trip.Id, trip.Name, trip.StartDate, trip.EndDate,
            trip.Days.OrderBy(d => d.DayNumber).Select(ToDayDto).ToList(),
            trip.Items.Where(i => i.ItineraryDayId is null)
                .OrderBy(i => i.SortOrder).Select(ToDestinationDto).ToList());
}
