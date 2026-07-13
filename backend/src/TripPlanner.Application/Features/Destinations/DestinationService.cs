using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Common.Validation;
using TripPlanner.Application.Features.Destinations.Dtos;
using TripPlanner.Domain.Entities;

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

    // TTLs per spec §11.2 — POI data is nearly static.
    private static readonly TimeSpan LocationsTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan AttractionsTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan DetailsTtl = TimeSpan.FromHours(24);

    // Entries stay resident well past their TTL so a provider outage can be
    // answered with stale data (spec §11.2 "stale-better-than-down"); the
    // retention window bounds memory growth.
    private static readonly TimeSpan CacheRetention = TimeSpan.FromDays(7);

    private readonly IApplicationDbContext _db;
    private readonly IDestinationProvider _provider;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _clock;
    private readonly IValidator<SearchLocationsRequest> _searchValidator;
    private readonly IValidator<GetAttractionsRequest> _attractionsValidator;
    private readonly IValidator<GetDestinationDetailsRequest> _detailsValidator;

    public DestinationService(
        IApplicationDbContext db,
        IDestinationProvider provider,
        IMemoryCache cache,
        TimeProvider clock,
        IValidator<SearchLocationsRequest> searchValidator,
        IValidator<GetAttractionsRequest> attractionsValidator,
        IValidator<GetDestinationDetailsRequest> detailsValidator)
    {
        _db = db;
        _provider = provider;
        _cache = cache;
        _clock = clock;
        _searchValidator = searchValidator;
        _attractionsValidator = attractionsValidator;
        _detailsValidator = detailsValidator;
    }

    /// <summary>
    /// A cached value plus WHEN it was fetched. Freshness is checked against
    /// the TTL by hand (instead of letting the cache evict) precisely so an
    /// expired entry is still readable as a stale fallback during an outage.
    /// </summary>
    private sealed record CacheEnvelope<T>(T Value, DateTimeOffset FetchedAt);

    /// <summary>
    /// Cache-aside over the provider (NFR1/NFR2): fresh hit → no provider
    /// call; miss/expired → fetch and re-cache; provider down but a stale
    /// entry exists → serve it rather than fail (spec §11.2).
    /// </summary>
    private async Task<T> GetCachedAsync<T>(string key, TimeSpan ttl, Func<Task<T>> fetchAsync)
    {
        var stale = _cache.TryGetValue(key, out CacheEnvelope<T>? envelope) ? envelope : null;
        if (stale is not null && _clock.GetUtcNow() - stale.FetchedAt < ttl)
        {
            return stale.Value;
        }

        try
        {
            var value = await fetchAsync();
            _cache.Set(key, new CacheEnvelope<T>(value, _clock.GetUtcNow()), CacheRetention);
            return value;
        }
        catch (HttpRequestException) when (stale is not null)
        {
            return stale.Value; // stale-better-than-down
        }
    }

    public async Task<IReadOnlyList<LocationSuggestionDto>> SearchLocationsAsync(string query, CancellationToken cancellationToken = default)
    {
        // F1/US1-US2 — the TRIMMED query must be ≥2 chars (spec §11.2), so
        // validate the same string we send to the provider, not the raw input.
        var trimmedQuery = query?.Trim() ?? string.Empty;
        await _searchValidator.ValidateAndThrowAppExceptionAsync(new SearchLocationsRequest(trimmedQuery), cancellationToken);

        // Key is the lowered query so "Paris" and "paris" share one entry. The
        // cached value is the POST-processed list — dedupe/rank/cap are
        // deterministic, so a hit skips that work too.
        var loweredQuery = trimmedQuery.ToLowerInvariant();
        return await GetCachedAsync<IReadOnlyList<LocationSuggestionDto>>(
            $"loc:{loweredQuery}", LocationsTtl, async () =>
            {
                var suggestions = await _provider.SearchLocationsAsync(trimmedQuery, cancellationToken);

                // Geoapify can return the same city under several place ids — dedupe on
                // what the user actually sees (name + country), rank exact and prefix
                // matches above substring hits (F1/US2 business rule), then cap at 5.
                return suggestions
                    .DistinctBy(s => (s.Name.ToLowerInvariant(), s.Country?.ToLowerInvariant()))
                    .OrderBy(s => RelevanceRank(s.Name, loweredQuery))
                    .Take(MaxLocationResults)
                    .ToList();
            });
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

        // Coordinates rounded to ~3 decimals (≈100 m) so near-identical map
        // positions share an entry; invariant formatting so the key doesn't
        // change with the server's locale ("16.05" vs "16,05").
        var key = FormattableString.Invariant(
            $"attr:{Math.Round(latitude, 3)}:{Math.Round(longitude, 3)}:{radiusKm}");
        return await GetCachedAsync<IReadOnlyList<DestinationSummaryDto>>(
            key, AttractionsTtl, async () =>
            {
                var attractions = await _provider.GetAttractionsAsync(latitude, longitude, radiusKm, cancellationToken);

                // "Recommended" default sort (spec §11.2): rating descending, unrated
                // last — which with Geoapify (no ratings) degrades to provider order.
                return attractions
                    .OrderBy(a => a.Rating is null)
                    .ThenByDescending(a => a.Rating)
                    .Take(MaxAttractionResults)
                    .ToList();
            });
    }

    public async Task<DestinationDetailsDto> GetDetailsAsync(string providerId, CancellationToken cancellationToken = default)
    {
        await _detailsValidator.ValidateAndThrowAppExceptionAsync(
            new GetDestinationDetailsRequest(providerId), cancellationToken);

        // Cache is checked by hand (not via GetCachedAsync) because a null
        // provider answer must NOT be cached and must fall through to the DB.
        var key = $"details:{providerId}";
        var stale = _cache.TryGetValue(key, out CacheEnvelope<DestinationDetailsDto>? envelope) ? envelope : null;
        if (stale is not null && _clock.GetUtcNow() - stale.FetchedAt < DetailsTtl)
        {
            return stale.Value;
        }

        // F2/US1 (spec §11.3): the provider has the freshest data, but it can
        // say "no such place" (null) or be unreachable (throws) — either way we
        // fall back to our own cache, never persisting here (DestinationService
        // never writes; only TripService.AddDestinationAsync upserts).
        DestinationDetailsDto? details;
        try
        {
            details = await _provider.GetDestinationDetailsAsync(providerId, cancellationToken);
        }
        catch (HttpRequestException)
        {
            if (stale is not null)
            {
                return stale.Value; // stale-better-than-down (spec §11.2)
            }

            details = null;
        }

        if (details is not null)
        {
            _cache.Set(key, new CacheEnvelope<DestinationDetailsDto>(details, _clock.GetUtcNow()), CacheRetention);
            return details;
        }

        // Saved-trip destinations must stay viewable even if the provider forgets
        // them (§8.4's snapshot rationale) — only a miss on BOTH sources is 404.
        var cached = await _db.Destinations
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ProviderId == providerId, cancellationToken);

        return cached?.ToDetailsDto() ?? throw new NotFoundException(nameof(Destination), providerId);
    }
}
