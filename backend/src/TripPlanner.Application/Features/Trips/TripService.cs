using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Common.Validation;
using TripPlanner.Application.Features.Destinations;
using TripPlanner.Application.Features.Trips.Dtos;
using TripPlanner.Domain.Entities;
using ValidationException = TripPlanner.Application.Common.Exceptions.ValidationException;

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
    private readonly IDestinationProvider _destinationProvider;
    private readonly IValidator<CreateTripRequest> _createTripValidator;
    private readonly IValidator<UpdateTripRequest> _updateTripValidator;
    private readonly IValidator<AddDestinationRequest> _addDestinationValidator;
    private readonly IValidator<UpdateItineraryItemRequest> _updateItemValidator;

    public TripService(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IDestinationProvider destinationProvider,
        IValidator<CreateTripRequest> createTripValidator,
        IValidator<UpdateTripRequest> updateTripValidator,
        IValidator<AddDestinationRequest> addDestinationValidator,
        IValidator<UpdateItineraryItemRequest> updateItemValidator)
    {
        _db = db;
        _currentUser = currentUser;
        _destinationProvider = destinationProvider;
        _createTripValidator = createTripValidator;
        _updateTripValidator = updateTripValidator;
        _addDestinationValidator = addDestinationValidator;
        _updateItemValidator = updateItemValidator;
    }

    public async Task<IReadOnlyList<TripSummaryDto>> GetMyTripsAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetRequiredUserId();

        // Projected straight to the DTO so SQL returns a COUNT per trip instead
        // of loading every itinerary item just to count it. SQLite cannot
        // ORDER BY a DateTimeOffset column, so the CreatedAt sort happens in
        // memory — a user's trip list is small.
        var rows = await _db.Trips
            .AsNoTracking()
            .Where(t => t.UserId == userId) // NFR 6: only the caller's trips.
            .Select(TripMappings.ToSummaryRowExpression)
            .ToListAsync(cancellationToken);

        return rows
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => r.Summary)
            .ToList();
    }

    public async Task<TripDetailDto> GetTripAsync(Guid tripId, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetRequiredUserId();

        // Includes follow the exact paths ToDetailDto reads. With AsNoTracking,
        // EF does NOT fix up navigations across separate include branches, so
        // Days must load their Items explicitly (not rely on Trip.Items).
        var trip = await _db.Trips
            .AsNoTracking()
            .Include(t => t.Days).ThenInclude(d => d.Items).ThenInclude(i => i.Destination)
            .Include(t => t.Items).ThenInclude(i => i.Destination)
            // NFR 6: filtering by owner AND id means "someone else's trip" and
            // "no such trip" are indistinguishable to the caller — both 404.
            .FirstOrDefaultAsync(t => t.Id == tripId && t.UserId == userId, cancellationToken)
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

        _db.Trips.Add(trip);
        await _db.SaveChangesAsync(cancellationToken);

        return trip.ToSummaryDto();
    }

    public async Task<TripDetailDto> UpdateTripAsync(Guid tripId, UpdateTripRequest request, CancellationToken cancellationToken = default)
    {
        await _updateTripValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

        var userId = _currentUser.GetRequiredUserId();

        // Tracked (no AsNoTracking): we are about to modify this graph.
        var trip = await _db.Trips
            .Include(t => t.Days).ThenInclude(d => d.Items)
            .Include(t => t.Items).ThenInclude(i => i.Destination)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(Trip), tripId);

        trip.Name = request.Name.Trim();
        // Domain rule: start ≤ end (throws DomainException -> 400).
        trip.SetDates(request.StartDate, request.EndDate);

        RegenerateDays(trip);

        await _db.SaveChangesAsync(cancellationToken);

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
            _db.ItineraryDays.Remove(day);
        }

        var existingDates = trip.Days.Select(d => d.Date).ToHashSet();
        foreach (var date in targetDates.Where(d => !existingDates.Contains(d)))
        {
            var day = new ItineraryDay { TripId = trip.Id, Date = date };
            trip.Days.Add(day);
            // Explicit Add: BaseEntity self-assigns the Guid key, so EF's graph
            // discovery would classify this as an EXISTING row (UPDATE, not INSERT).
            _db.ItineraryDays.Add(day);
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

        var trip = await _db.Trips
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.UserId == userId, cancellationToken)
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
        _db.ItineraryItems.Add(item);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
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

        // All items load because resequencing touches BOTH buckets (source and
        // target); Destination loads because the response DTO reads it.
        var trip = await _db.Trips
            .Include(t => t.Items).ThenInclude(i => i.Destination)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.UserId == userId, cancellationToken)
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
            await _db.SaveChangesAsync(cancellationToken); // single save: both buckets move atomically
        }
        catch (DbUpdateException)
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

        var belongs = await _db.ItineraryDays
            .AnyAsync(d => d.Id == itineraryDayId && d.TripId == tripId, cancellationToken);
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
        var cached = await _db.Destinations
            .FirstOrDefaultAsync(d => d.ProviderId == providerId, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var details = await _destinationProvider.GetDestinationDetailsAsync(providerId, cancellationToken)
            ?? throw new NotFoundException(nameof(Destination), providerId);

        var destination = details.ToEntity();
        _db.Destinations.Add(destination);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return destination;
        }
        catch (DbUpdateException)
        {
            // Unique index on ProviderId: a concurrent request inserted the same
            // place first. Discard our copy and use the winner's row.
            _db.Destinations.Remove(destination);
            return await _db.Destinations.FirstAsync(d => d.ProviderId == providerId, cancellationToken);
        }
    }

    public async Task RemoveDestinationAsync(Guid tripId, Guid itemId, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetRequiredUserId();

        var item = await _db.ItineraryItems.FirstOrDefaultAsync(
            i => i.Id == itemId && i.TripId == tripId && i.Trip!.UserId == userId, // NFR 6
            cancellationToken)
            ?? throw new NotFoundException(nameof(ItineraryItem), itemId);

        // SortOrder gaps left by the removal are harmless — ordering is relative.
        _db.ItineraryItems.Remove(item);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
