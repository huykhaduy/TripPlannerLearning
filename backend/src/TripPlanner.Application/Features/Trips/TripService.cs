using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Extensions;
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
        // Domain rules: start ≤ end (throws DomainException -> 400), and the
        // itinerary days are regenerated for the new range (F3/US2, spec §11.1).
        trip.SetDates(request.StartDate, request.EndDate);

        await _trips.UpdateAsync(trip, cancellationToken);

        return trip.ToDetailDto();
    }

    public async Task<TripDestinationDto> AddDestinationAsync(Guid tripId, AddDestinationRequest request, CancellationToken cancellationToken = default)
    {
        await AddDestinationValidator.ValidateAndThrowAppExceptionAsync(request, cancellationToken);

        var userId = _currentUser.GetRequiredUserId();

        var trip = await _trips.GetForUpdateAsync(tripId, userId, cancellationToken)
            ?? throw new NotFoundException(nameof(Trip), tripId);

        await EnsureDayBelongsToTripAsync(request.ItineraryDayId, trip.Id, cancellationToken);

        // ORDER MATTERS: this must stay ABOVE the trip.Items.Add below. Every
        // repository shares one scoped DbContext and each write method calls
        // SaveChanges on all of it, so once the trip graph is mutated in memory,
        // the destination insert inside here would flush that half-finished change
        // with it. Adding the destination while the trip is still untouched keeps
        // the two saves independent.
        var destination = await GetOrCreateDestinationAsync(request.ProviderId, cancellationToken);

        EnsureNotDuplicate(trip, destination.Id, request.ItineraryDayId);

        var item = new ItineraryItem
        {
            TripId = trip.Id,
            DestinationId = destination.Id,
            Destination = destination,
            ItineraryDayId = request.ItineraryDayId,
            SortOrder = trip.NextSortOrderIn(request.ItineraryDayId),
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

        trip.MoveItem(item, request.ItineraryDayId, request.SortOrder);

        // One save: both affected buckets move atomically.
        await SaveWithDuplicateGuardAsync(trip, cancellationToken);

        return item.ToDestinationDto();
    }

    /// <summary>
    /// Turns the aggregate's duplicate rule (US4/US6) into this layer's vocabulary.
    /// <see cref="Trip.HasDestinationIn"/> owns the rule; a duplicate being an HTTP
    /// 409 is a request-level concern, which is why the exception is raised here and
    /// not in the Domain.
    /// </summary>
    private static void EnsureNotDuplicate(Trip trip, Guid destinationId, Guid? itineraryDayId, Guid? excludeItemId = null)
    {
        if (trip.HasDestinationIn(destinationId, itineraryDayId, excludeItemId))
        {
            throw new ConflictException(DuplicateDestinationMessage);
        }
    }

    /// <summary>
    /// Saves, translating a unique-index violation on (ItineraryDayId, DestinationId)
    /// into the same conflict <see cref="EnsureNotDuplicate"/> raises — the index is
    /// the backstop for a concurrent request slipping in between check and save.
    ///
    /// Only for the add/move paths. <c>UpdateTripAsync</c> saves directly, because a
    /// date change cannot violate that index and reporting a duplicate would be a lie.
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
    /// The provider's image data is sparse, so fall back to a Serper search by name —
    /// the same source the attraction cards use, so a saved destination isn't stuck
    /// showing the placeholder icon. Best-effort by design: a Serper outage leaves the
    /// destination saved without a photo rather than failing the add.
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
        catch (ExternalServiceUnavailableException)
        {
            // Already logged by SerperImageClient; the destination is still saved.
        }
    }

    public async Task RemoveDestinationAsync(Guid tripId, Guid itemId, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetRequiredUserId();

        var item = await _trips.GetOwnedItemAsync(tripId, itemId, userId, cancellationToken) // NFR 6
            ?? throw new NotFoundException(nameof(ItineraryItem), itemId);

        // SortOrder gaps left by the removal are harmless — ordering is relative.
        await _trips.RemoveItemAsync(item, cancellationToken);
    }
}
