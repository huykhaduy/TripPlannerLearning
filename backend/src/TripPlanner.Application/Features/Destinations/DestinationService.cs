using FluentValidation;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Common.Validation;
using TripPlanner.Application.Features.Destinations.Dtos;

namespace TripPlanner.Application.Features.Destinations;

/// <summary>
/// STUB — students implement this (Features 1 &amp; 2).
///
/// Suggested approach:
///   * call <see cref="IDestinationProvider"/> for raw data;
///   * de-duplicate, rank by relevance, cap results (F1 business rules);
///   * optionally cache popular searches to meet the performance NFRs;
///   * map provider models to the DTOs in this folder.
///
/// Every method currently throws <see cref="NotImplementedException"/> so the
/// project compiles and the API surface is visible in Swagger from day one.
/// </summary>
public class DestinationService : IDestinationService
{
    private const int MaxLocationResults = 5;
    private const int MaxAttractionResults = 20;

    private readonly IDestinationProvider _provider;
    private readonly IValidator<SearchLocationsRequest> _searchValidator;
    private readonly IValidator<GetAttractionsRequest> _attractionsValidator;

    public DestinationService(
        IDestinationProvider provider,
        IValidator<SearchLocationsRequest> searchValidator,
        IValidator<GetAttractionsRequest> attractionsValidator)
    {
        _provider = provider;
        _searchValidator = searchValidator;
        _attractionsValidator = attractionsValidator;
    }

    public async Task<IReadOnlyList<LocationSuggestionDto>> SearchLocationsAsync(string query, CancellationToken cancellationToken = default)
    {
        // F1/US1-US2 — the TRIMMED query must be ≥2 chars (spec §11.2), so
        // validate the same string we send to the provider, not the raw input.
        var trimmedQuery = query?.Trim() ?? string.Empty;
        await _searchValidator.ValidateAndThrowAppExceptionAsync(new SearchLocationsRequest(trimmedQuery), cancellationToken);

        var suggestions = await _provider.SearchLocationsAsync(trimmedQuery, cancellationToken);

        // Geoapify can return the same city under several place ids — dedupe on
        // what the user actually sees (name + country), rank exact and prefix
        // matches above substring hits (F1/US2 business rule), then cap at 5.
        var loweredQuery = trimmedQuery.ToLowerInvariant();
        return suggestions
            .DistinctBy(s => (s.Name.ToLowerInvariant(), s.Country?.ToLowerInvariant()))
            .OrderBy(s => RelevanceRank(s.Name, loweredQuery))
            .Take(MaxLocationResults)
            .ToList();
    }

    /// <summary>Exact match first, then prefix matches, then everything else.</summary>
    private static int RelevanceRank(string name, string loweredQuery)
    {
        var loweredName = name.ToLowerInvariant();
        if (loweredName == loweredQuery)
        {
            return 0;
        }

        return loweredName.StartsWith(loweredQuery, StringComparison.Ordinal) ? 1 : 2;
    }

    public async Task<IReadOnlyList<DestinationSummaryDto>> GetAttractionsAsync(double latitude, double longitude, double radiusKm, CancellationToken cancellationToken = default)
    {
        // F1/US3 — coordinates on the globe, 0 < radius ≤ 50 km (validator -> HTTP 400).
        await _attractionsValidator.ValidateAndThrowAppExceptionAsync(
            new GetAttractionsRequest(latitude, longitude, radiusKm), cancellationToken);

        var attractions = await _provider.GetAttractionsAsync(latitude, longitude, radiusKm, cancellationToken);

        // "Recommended" default sort (spec §11.2): rating descending, unrated
        // last — which with Geoapify (no ratings) degrades to provider order.
        return attractions
            .OrderBy(a => a.Rating is null)
            .ThenByDescending(a => a.Rating)
            .Take(MaxAttractionResults)
            .ToList();
    }

    public Task<DestinationDetailsDto> GetDetailsAsync(string providerId, CancellationToken cancellationToken = default)
    {
        // TODO: F2/US1 — fetch full details by provider id; the view must still
        //       open when optional fields (photos/hours/map) are missing.
        throw new NotImplementedException("Implement destination details — see Feature 2, US1.");
    }
}
