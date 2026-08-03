using System.Text.Json;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Common.Validation;
using TripPlanner.Application.Features.Destinations;
using TripPlanner.Application.Features.Trips.Dtos;
using TripPlanner.Application.Features.Trips.Validators;
using TripPlanner.Domain.Entities;
using ValidationException = TripPlanner.Application.Common.Exceptions.ValidationException;

namespace TripPlanner.Application.Features.Trips;

/// <summary>
/// Trip planner (Feature 3): CRUD, date-driven itinerary generation, and
/// destination scheduling/reordering across days.
///
/// Every read/write path resolves the owner from
/// <see cref="ICurrentUserService.GetRequiredUserId"/> and filters by it —
/// never trusts a trip id alone (NFR 6 / authorization). Date changes (US2)
/// regenerate <c>ItineraryDay</c> rows for the new range via
/// <c>Trip.SetDates</c>.
/// </summary>
public class TripService : ITripService
{
    /// <summary>
    /// Shared by the in-memory duplicate check and the unique-index backstop, so the
    /// caller cannot tell which one rejected the request — they mean the same thing.
    /// </summary>
    private const string DuplicateDestinationMessage = "This destination is already in that part of the trip.";

    // Stateless rule declarations with no dependencies — shared instances rather than
    // constructor parameters. See AuthService for the reasoning.
    private static readonly CreateTripRequestValidator CreateTripValidator = new();
    private static readonly UpdateTripRequestValidator UpdateTripValidator = new();
    private static readonly AddDestinationRequestValidator AddDestinationValidator = new();
    private static readonly UpdateItineraryItemRequestValidator UpdateItemValidator = new();

    private readonly ITripRepository _trips;
    private readonly IDestinationRepository _destinations;
    private readonly ICurrentUserService _currentUser;
    private readonly IDestinationProvider _destinationProvider;
    private readonly IImageSearchProvider _imageSearch;

    public TripService(
        ITripRepository trips,
        IDestinationRepository destinations,
        ICurrentUserService currentUser,
        IDestinationProvider destinationProvider,
        IImageSearchProvider imageSearch)
    {
        _trips = trips;
        _destinations = destinations;
        _currentUser = currentUser;
        _destinationProvider = destinationProvider;
        _imageSearch = imageSearch;
    }

    public async Task<IReadOnlyList<TripSummaryDto>> GetMyTripsAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetRequiredUserId();

        var rows = await _trips.GetSummaryRowsForUserAsync(userId, cancellationToken);

        // Sorted in memory rather than in SQL — a user's trip list is small,
        // and this avoids re-querying CreatedAt as a separate ORDER BY.
        return rows
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => r.Summary)
            .ToList();
    }

    public async Task<TripDetailDto> GetTripAsync(Guid tripId, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetRequiredUserId();

        // NFR 6: filtering by owner AND id means "someone else's trip" and
        // "no such trip" are indistinguishable to the caller — both 404.
        var trip = await _trips.GetDetailsAsync(tripId, userId, cancellationToken)
            ?? throw new NotFoundException(nameof(Trip), tripId);

        return trip.ToDetailDto();
    }

    public async Task<TripSummaryDto> CreateTripAsync(CreateTripRequest request, CancellationToken cancellationToken = default)
    {
        // F3/US1 — name is required (CreateTripRequestValidator -> HTTP 400).
        await CreateTripValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

        var trip = new Trip
        {
            Name = request.Name.Trim(),
            // NFR 6: the trip belongs to the caller; throws 401 when anonymous.
            UserId = _currentUser.GetRequiredUserId(),
        };

        await _trips.AddAsync(trip, cancellationToken);

        return trip.ToSummaryDto();
    }

    public async Task<TripDetailDto> UpdateTripAsync(Guid tripId, UpdateTripRequest request, CancellationToken cancellationToken = default)
    {
        await UpdateTripValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

        var userId = _currentUser.GetRequiredUserId();

        var trip = await _trips.GetForUpdateAsync(tripId, userId, cancellationToken)
            ?? throw new NotFoundException(nameof(Trip), tripId);

        trip.Name = request.Name.Trim();
        // Domain rule: start ≤ end (throws DomainException -> 400).
        trip.SetDates(request.StartDate, request.EndDate);

        RegenerateDays(trip);

        await _trips.UpdateAsync(trip, cancellationToken);

        return trip.ToDetailDto();
    }

    /// <summary>
    /// F3/US2 day regeneration (spec §11.1): keep days still inside the date
    /// range (preserving their scheduled items), delete days that fell out of
    /// it (their items return to Saved Places), create days for new dates,
    /// then renumber chronologically.
    /// </summary>
    private void RegenerateDays(Trip trip)
    {
        var targetDates = new HashSet<DateOnly>();
        if (trip.StartDate is { } start && trip.EndDate is { } end)
        {
            for (var date = start; date <= end; date = date.AddDays(1))
            {
                targetDates.Add(date);
            }
        }

        foreach (var day in trip.Days.Where(d => !targetDates.Contains(d.Date)).ToList())
        {
            // Mirror the DB's SetNull cascade in memory so the DTO we return
            // already shows these items back in Saved Places.
            foreach (var item in day.Items)
            {
                item.ItineraryDayId = null;
            }

            // Trip.Days is configured OnDelete(Cascade), so removing the day from
            // the tracked collection marks the row deleted.
            trip.Days.Remove(day);
        }

        var existingDates = trip.Days.Select(d => d.Date).ToHashSet();
        foreach (var date in targetDates.Where(d => !existingDates.Contains(d)))
        {
            trip.Days.Add(new ItineraryDay { TripId = trip.Id, Date = date });
        }

        var dayNumber = 1;
        foreach (var day in trip.Days.OrderBy(d => d.Date))
        {
            day.DayNumber = dayNumber++;
        }
    }

    public async Task<TripDestinationDto> AddDestinationAsync(Guid tripId, AddDestinationRequest request, CancellationToken cancellationToken = default)
    {
        await AddDestinationValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

        var userId = _currentUser.GetRequiredUserId();

        var trip = await _trips.GetForUpdateAsync(tripId, userId, cancellationToken)
            ?? throw new NotFoundException(nameof(Trip), tripId);

        await EnsureDayBelongsToTripAsync(request.ItineraryDayId, trip.Id, cancellationToken);

        var destination = await GetOrCreateDestinationAsync(request.ProviderId, cancellationToken);

        EnsureNotDuplicate(trip, destination.Id, request.ItineraryDayId);

        var bucket = trip.Items.Where(i => i.ItineraryDayId == request.ItineraryDayId).ToList();

        var item = new ItineraryItem
        {
            TripId = trip.Id,
            DestinationId = destination.Id,
            Destination = destination,
            ItineraryDayId = request.ItineraryDayId,
            SortOrder = bucket.Count == 0 ? 0 : bucket.Max(i => i.SortOrder) + 1,
        };
        trip.Items.Add(item);

        await SaveWithDuplicateGuardAsync(trip, cancellationToken);

        return item.ToDestinationDto();
    }

    public async Task<TripDestinationDto> UpdateItineraryItemAsync(Guid tripId, Guid itemId, UpdateItineraryItemRequest request, CancellationToken cancellationToken = default)
    {
        await UpdateItemValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

        var userId = _currentUser.GetRequiredUserId();

        var trip = await _trips.GetForUpdateAsync(tripId, userId, cancellationToken)
            ?? throw new NotFoundException(nameof(Trip), tripId);

        var item = trip.Items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new NotFoundException(nameof(ItineraryItem), itemId);

        await EnsureDayBelongsToTripAsync(request.ItineraryDayId, trip.Id, cancellationToken);

        // The moved item is exempt from its own duplicate check, so reordering
        // within the same day passes.
        EnsureNotDuplicate(trip, item.DestinationId, request.ItineraryDayId, excludeItemId: item.Id);

        MoveItem(trip, item, request.ItineraryDayId, request.SortOrder);

        // One save: both affected buckets move atomically.
        await SaveWithDuplicateGuardAsync(trip, cancellationToken);

        return item.ToDestinationDto();
    }

    /// <summary>
    /// Duplicate rule (US4/US6): a destination appears at most once per day, and at
    /// most once in the Saved Places bucket. Pass <paramref name="excludeItemId"/>
    /// when moving an existing item so it cannot conflict with itself.
    /// </summary>
    private static void EnsureNotDuplicate(Trip trip, Guid destinationId, Guid? itineraryDayId, Guid? excludeItemId = null)
    {
        if (trip.Items.Any(i => i.Id != excludeItemId
                && i.DestinationId == destinationId
                && i.ItineraryDayId == itineraryDayId))
        {
            throw new ConflictException(DuplicateDestinationMessage);
        }
    }

    /// <summary>
    /// Saves the trip aggregate, translating a unique-index violation on
    /// (ItineraryDayId, DestinationId) into the same conflict
    /// <see cref="EnsureNotDuplicate"/> raises — that index is the backstop for a
    /// concurrent request slipping the same destination in between the check and
    /// the save.
    ///
    /// Only for the two paths that add or move an item. <c>UpdateTripAsync</c>
    /// deliberately saves directly: a date change cannot violate that index, so
    /// reporting "duplicate destination" there would be a lie.
    /// </summary>
    private async Task SaveWithDuplicateGuardAsync(Trip trip, CancellationToken cancellationToken)
    {
        try
        {
            await _trips.UpdateAsync(trip, cancellationToken);
        }
        catch (ConcurrencyException)
        {
            throw new ConflictException(DuplicateDestinationMessage);
        }
    }

    /// <summary>
    /// Spec §11.1 US4-US6: move an item into <paramref name="targetDayId"/> (null =
    /// Saved Places) at <paramref name="sortOrder"/>, then renumber both affected
    /// buckets 0..n so values stay dense. The position is clamped, so "99" means last.
    /// </summary>
    private static void MoveItem(Trip trip, ItineraryItem item, Guid? targetDayId, int sortOrder)
    {
        var sourceDayId = item.ItineraryDayId;
        item.ItineraryDayId = targetDayId;

        var target = trip.Items
            .Where(i => i.Id != item.Id && i.ItineraryDayId == targetDayId)
            .OrderBy(i => i.SortOrder)
            .ToList();
        target.Insert(Math.Min(sortOrder, target.Count), item);
        Resequence(target);

        // The bucket the item left keeps its relative order but closes the gap.
        if (sourceDayId != targetDayId)
        {
            Resequence(trip.Items
                .Where(i => i.Id != item.Id && i.ItineraryDayId == sourceDayId)
                .OrderBy(i => i.SortOrder)
                .ToList());
        }
    }

    private static void Resequence(List<ItineraryItem> bucket)
    {
        for (var position = 0; position < bucket.Count; position++)
        {
            bucket[position].SortOrder = position;
        }
    }

    /// <summary>
    /// A target day, when given, must exist and belong to THIS trip
    /// (spec §11.1 rule 2). Also used by the schedule/move endpoint (US4-US6).
    /// </summary>
    private async Task EnsureDayBelongsToTripAsync(Guid? itineraryDayId, Guid tripId, CancellationToken cancellationToken)
    {
        if (itineraryDayId is null)
        {
            return; // no day targeted -> Saved Places, nothing to check
        }

        var belongs = await _trips.DayBelongsToTripAsync(itineraryDayId.Value, tripId, cancellationToken);
        if (!belongs)
        {
            throw ValidationException.ForProperty(
                nameof(AddDestinationRequest.ItineraryDayId),
                "The itinerary day does not belong to this trip.");
        }
    }

    /// <summary>
    /// Upsert of the <see cref="Destination"/> cache (spec §11.0-6): rows are
    /// created lazily the first time any user adds a place to any trip. A fresh
    /// ProviderId is the normal case — never 404 on a cache miss; only when the
    /// external provider does not know the id either.
    /// </summary>
    private async Task<Destination> GetOrCreateDestinationAsync(string providerId, CancellationToken cancellationToken)
    {
        var cached = await _destinations.GetByProviderIdAsync(providerId, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var details = await _destinationProvider.GetDestinationDetailsAsync(providerId, cancellationToken)
            ?? throw new NotFoundException(nameof(Destination), providerId);

        var destination = details.ToEntity();

        await TryFillMissingImageAsync(destination, cancellationToken);

        try
        {
            await _destinations.AddAsync(destination, cancellationToken);
            return destination;
        }
        catch (ConcurrencyException)
        {
            // Unique index on ProviderId: a concurrent request inserted the same
            // place first. Use the winner's row instead of ours.
            return await _destinations.GetByProviderIdAsync(providerId, cancellationToken)
                ?? throw new InvalidOperationException($"Destination '{providerId}' vanished after a concurrency conflict.");
        }
    }

    /// <summary>
    /// The provider's own image data is sparse (Geoapify only has wiki_and_media for
    /// some places), so fall back to a Serper image search by name — the same source
    /// the attraction cards use, so a saved destination isn't stuck showing the
    /// placeholder icon everywhere.
    ///
    /// Best-effort by design: a Serper outage leaves the destination saved without a
    /// photo rather than failing the add. Does nothing if the provider already gave
    /// us an image.
    /// </summary>
    private async Task TryFillMissingImageAsync(Destination destination, CancellationToken cancellationToken)
    {
        if (destination.ImageUrl is not null)
        {
            return;
        }

        try
        {
            destination.ImageUrl = await _imageSearch.SearchImageAsync(destination.Name, cancellationToken);
        }
        catch (Exception ex) when (IsTransientExternalFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            // Already logged by SerperImageClient; the destination is still saved.
        }
    }

    /// <summary>
    /// True for the external-call failure modes treated as "no image found"
    /// rather than "the whole request must fail": connection failures,
    /// HttpClient timeouts (surfaced as TaskCanceledException, not
    /// HttpRequestException), and an unparseable response body.
    /// </summary>
    private static bool IsTransientExternalFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException;

    public async Task RemoveDestinationAsync(Guid tripId, Guid itemId, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetRequiredUserId();

        var item = await _trips.GetOwnedItemAsync(tripId, itemId, userId, cancellationToken) // NFR 6
            ?? throw new NotFoundException(nameof(ItineraryItem), itemId);

        // SortOrder gaps left by the removal are harmless — ordering is relative.
        await _trips.RemoveItemAsync(item, cancellationToken);
    }
}
