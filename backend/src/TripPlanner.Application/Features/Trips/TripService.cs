using System.Text.Json;
using FluentValidation;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Common.Validation;
using TripPlanner.Application.Features.Destinations;
using TripPlanner.Application.Features.Trips.Dtos;
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
    private readonly ITripRepository _trips;
    private readonly IRepository<ItineraryDay> _itineraryDays;
    private readonly IRepository<ItineraryItem> _itineraryItems;
    private readonly IDestinationRepository _destinations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IDestinationProvider _destinationProvider;
    private readonly IImageSearchProvider _imageSearch;
    private readonly IValidator<CreateTripRequest> _createTripValidator;
    private readonly IValidator<UpdateTripRequest> _updateTripValidator;
    private readonly IValidator<AddDestinationRequest> _addDestinationValidator;
    private readonly IValidator<UpdateItineraryItemRequest> _updateItemValidator;

    public TripService(
        ITripRepository trips,
        IRepository<ItineraryDay> itineraryDays,
        IRepository<ItineraryItem> itineraryItems,
        IDestinationRepository destinations,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IDestinationProvider destinationProvider,
        IImageSearchProvider imageSearch,
        IValidator<CreateTripRequest> createTripValidator,
        IValidator<UpdateTripRequest> updateTripValidator,
        IValidator<AddDestinationRequest> addDestinationValidator,
        IValidator<UpdateItineraryItemRequest> updateItemValidator)
    {
        _trips = trips;
        _itineraryDays = itineraryDays;
        _itineraryItems = itineraryItems;
        _destinations = destinations;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _destinationProvider = destinationProvider;
        _imageSearch = imageSearch;
        _createTripValidator = createTripValidator;
        _updateTripValidator = updateTripValidator;
        _addDestinationValidator = addDestinationValidator;
        _updateItemValidator = updateItemValidator;
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
        await _createTripValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

        var trip = new Trip
        {
            Name = request.Name.Trim(),
            // NFR 6: the trip belongs to the caller; throws 401 when anonymous.
            UserId = _currentUser.GetRequiredUserId(),
        };

        _trips.Add(trip);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return trip.ToSummaryDto();
    }

    public async Task<TripDetailDto> UpdateTripAsync(Guid tripId, UpdateTripRequest request, CancellationToken cancellationToken = default)
    {
        await _updateTripValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

        var userId = _currentUser.GetRequiredUserId();

        var trip = await _trips.GetTrackedWithFullGraphAsync(tripId, userId, cancellationToken)
            ?? throw new NotFoundException(nameof(Trip), tripId);

        trip.Name = request.Name.Trim();
        // Domain rule: start ≤ end (throws DomainException -> 400).
        trip.SetDates(request.StartDate, request.EndDate);

        RegenerateDays(trip);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

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

            trip.Days.Remove(day);
            _itineraryDays.Remove(day);
        }

        var existingDates = trip.Days.Select(d => d.Date).ToHashSet();
        foreach (var date in targetDates.Where(d => !existingDates.Contains(d)))
        {
            var day = new ItineraryDay { TripId = trip.Id, Date = date };
            trip.Days.Add(day);
            // Explicit Add: BaseEntity self-assigns the Guid key, so EF's graph
            // discovery would classify this as an EXISTING row (UPDATE, not INSERT).
            _itineraryDays.Add(day);
        }

        var dayNumber = 1;
        foreach (var day in trip.Days.OrderBy(d => d.Date))
        {
            day.DayNumber = dayNumber++;
        }
    }

    public async Task<TripDestinationDto> AddDestinationAsync(Guid tripId, AddDestinationRequest request, CancellationToken cancellationToken = default)
    {
        await _addDestinationValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

        var userId = _currentUser.GetRequiredUserId();

        var trip = await _trips.GetTrackedWithFullGraphAsync(tripId, userId, cancellationToken)
            ?? throw new NotFoundException(nameof(Trip), tripId);

        await EnsureDayBelongsToTripAsync(request.ItineraryDayId, trip.Id, cancellationToken);

        var destination = await GetOrCreateDestinationAsync(request.ProviderId, cancellationToken);

        // Duplicate rule (US4/US6): once per day — and per Saved Places bucket.
        if (trip.Items.Any(i => i.DestinationId == destination.Id && i.ItineraryDayId == request.ItineraryDayId))
        {
            throw new ConflictException("This destination is already in that part of the trip.");
        }

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
        _itineraryItems.Add(item);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyException)
        {
            // Unique index (ItineraryDayId, DestinationId): a concurrent request
            // added the same destination between our check and the save.
            throw new ConflictException("This destination is already in that part of the trip.");
        }

        return item.ToDestinationDto();
    }

    public async Task<TripDestinationDto> UpdateItineraryItemAsync(Guid tripId, Guid itemId, UpdateItineraryItemRequest request, CancellationToken cancellationToken = default)
    {
        await _updateItemValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

        var userId = _currentUser.GetRequiredUserId();

        var trip = await _trips.GetTrackedWithFullGraphAsync(tripId, userId, cancellationToken)
            ?? throw new NotFoundException(nameof(Trip), tripId);

        var item = trip.Items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new NotFoundException(nameof(ItineraryItem), itemId);

        await EnsureDayBelongsToTripAsync(request.ItineraryDayId, trip.Id, cancellationToken);

        // Duplicate rule on the TARGET day (US4/US6) — the moved item itself is
        // exempt, so reordering within the same day passes this check.
        if (trip.Items.Any(i => i.Id != item.Id
                && i.DestinationId == item.DestinationId
                && i.ItineraryDayId == request.ItineraryDayId))
        {
            throw new ConflictException("This destination is already in that part of the trip.");
        }

        var sourceDayId = item.ItineraryDayId;
        item.ItineraryDayId = request.ItineraryDayId;

        // Spec §11.1 US4-US6: insert at the requested position, then renumber
        // 0..n so values stay dense. Clamp so "position 99" means "last".
        var target = trip.Items
            .Where(i => i.Id != item.Id && i.ItineraryDayId == request.ItineraryDayId)
            .OrderBy(i => i.SortOrder)
            .ToList();
        target.Insert(Math.Min(request.SortOrder, target.Count), item);
        Resequence(target);

        // The bucket the item left keeps its relative order but closes the gap.
        if (sourceDayId != request.ItineraryDayId)
        {
            Resequence(trip.Items
                .Where(i => i.Id != item.Id && i.ItineraryDayId == sourceDayId)
                .OrderBy(i => i.SortOrder)
                .ToList());
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken); // single save: both buckets move atomically
        }
        catch (ConcurrencyException)
        {
            // Unique index (ItineraryDayId, DestinationId): a concurrent request
            // put the same destination into the target day between check and save.
            throw new ConflictException("This destination is already in that part of the trip.");
        }

        return item.ToDestinationDto();
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

        // The provider's own image data is sparse (Geoapify only has
        // wiki_and_media for some places) — fall back to a Serper image search
        // by name, same source the attraction cards use, so a saved
        // destination isn't stuck showing the placeholder icon everywhere.
        if (destination.ImageUrl is null)
        {
            try
            {
                destination.ImageUrl = await _imageSearch.SearchImageAsync(destination.Name, cancellationToken);
            }
            catch (Exception ex) when (IsTransientExternalFailure(ex) && !cancellationToken.IsCancellationRequested)
            {
                // Serper down, timed out, or returned something unparseable —
                // the destination is still saved, just without a photo.
            }
        }

        _destinations.Add(destination);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return destination;
        }
        catch (ConcurrencyException)
        {
            // Unique index on ProviderId: a concurrent request inserted the same
            // place first. Discard our copy and use the winner's row.
            _destinations.Remove(destination);
            return await _destinations.GetByProviderIdAsync(providerId, cancellationToken)
                ?? throw new InvalidOperationException($"Destination '{providerId}' vanished after a concurrency conflict.");
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
        _itineraryItems.Remove(item);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
