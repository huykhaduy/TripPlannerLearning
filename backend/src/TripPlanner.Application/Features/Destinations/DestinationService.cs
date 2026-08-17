using System.Collections.Concurrent;
using TripPlanner.Application.Common.Caching;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.Application.Common.Validation;
using TripPlanner.Application.Features.Destinations.Dtos;
using TripPlanner.Application.Features.Destinations.Validators;
using TripPlanner.Domain.Entities;

namespace TripPlanner.Application.Features.Destinations;

/// <summary>
/// Destination search, attractions, and details (Features 1 &amp; 2).
///
/// Calls <see cref="IDestinationProvider"/> for raw data, de-duplicates and
/// ranks/caps results per the F1 business rules, and maps provider models to
/// the DTOs in this folder. <see cref="Destination"/> rows are a cache of
/// external-provider data keyed by <c>ProviderId</c> — this service never
/// persists a new row itself; see <see cref="Trips.TripService.GetOrCreateDestinationAsync"/>
/// for the upsert-on-first-add path.
/// </summary>
public class DestinationService : IDestinationService
{
    private const int MaxLocationResults = 5;
    private const int MaxAttractionResults = 20;

    // The destination provider's own image data is sparse (Geoapify only has
    // wiki_and_media for some places), so filling in thumbnails means one
    // extra image-search lookup per result — capped in parallel so a
    // 20-result page doesn't hammer the image search provider (a paid,
    // per-query API) all at once.
    private const int ImageFetchConcurrency = 5;

    // F2/US2 — how many photos the details view's carousel gets per place.
    private const int DetailsPhotoCount = 5;

    // How long a resolved (or "nothing found") image is cached per place, so
    // a paid Serper query is never repeated for the same attraction.
    private static readonly TimeSpan ImageTtl = TimeSpan.FromHours(24);

    // TTLs per spec §11.2 — POI data is nearly static.
    private static readonly TimeSpan LocationsTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan AttractionsTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan DetailsTtl = TimeSpan.FromHours(24);

    // Stateless rule declarations with no dependencies — shared instances rather than
    // constructor parameters. See AuthService for the reasoning.
    private static readonly SearchLocationsRequestValidator SearchValidator = new();
    private static readonly GetAttractionsRequestValidator AttractionsValidator = new();
    private static readonly GetDestinationDetailsRequestValidator DetailsValidator = new();

    private readonly IDestinationRepository _destinations;
    private readonly IDestinationProvider _provider;
    private readonly IImageSearchProvider _imageSearch;
    private readonly IStaleTolerantCache _cache;
    private readonly TimeProvider _clock;

    public DestinationService(
        IDestinationRepository destinations,
        IDestinationProvider provider,
        IImageSearchProvider imageSearch,
        IStaleTolerantCache cache,
        TimeProvider clock)
    {
        _destinations = destinations;
        _provider = provider;
        _imageSearch = imageSearch;
        _cache = cache;
        _clock = clock;
    }

    /// <summary>
    /// Cache-aside over the provider (NFR1/NFR2): fresh hit → no provider
    /// call; miss/expired → fetch and re-cache; provider down but a stale
    /// entry exists → serve it rather than fail (spec §11.2).
    ///
    /// Only an adapter-reported outage falls back. A serialize failure inside
    /// <see cref="IStaleTolerantCache.SetAsync{T}"/> surfaces as the bug it is, instead
    /// of being mistaken for a provider failure and answered with stale data.
    /// </summary>
    private async Task<T> GetCachedAsync<T>(string key, TimeSpan ttl, Func<Task<T>> fetchAsync, CancellationToken cancellationToken)
    {
        var stale = await _cache.TryGetAsync<T>(key, cancellationToken);
        if (stale is not null && _clock.GetUtcNow() - stale.FetchedAt < ttl)
        {
            return stale.Value;
        }

        try
        {
            var value = await fetchAsync();
            await _cache.SetAsync(key, new CacheEnvelope<T>(value, _clock.GetUtcNow()), cancellationToken);
            return value;
        }
        catch (ExternalServiceUnavailableException) when (stale is not null)
        {
            return stale.Value; // stale-better-than-down
        }
    }

    public async Task<IReadOnlyList<LocationSuggestionDto>> SearchLocationsAsync(string query, CancellationToken cancellationToken = default)
    {
        // F1/US1-US2 — the TRIMMED query must be ≥2 chars (spec §11.2), so
        // validate the same string we send to the provider, not the raw input.
        var trimmedQuery = query?.Trim() ?? string.Empty;
        await SearchValidator.ValidateAndThrowAppExceptionAsync(new SearchLocationsRequest(trimmedQuery), cancellationToken);

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
            }, cancellationToken);
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
        await AttractionsValidator.ValidateAndThrowAppExceptionAsync(
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
                // Ranked/capped BEFORE image enrichment so we only spend extra provider
                // calls on the ≤20 results we actually return.
                var ranked = attractions
                    .OrderBy(a => a.Rating is null)
                    .ThenByDescending(a => a.Rating)
                    .Take(MaxAttractionResults)
                    .ToList();

                return await EnrichWithImagesAsync(ranked, cancellationToken);
            }, cancellationToken);
    }

    /// <summary>
    /// Fills in each attraction's ImageUrl. A failed lookup for one place just
    /// leaves that one imageless — it must never take down the whole list.
    /// </summary>
    private async Task<IReadOnlyList<DestinationSummaryDto>> EnrichWithImagesAsync(
        IReadOnlyList<DestinationSummaryDto> attractions, CancellationToken cancellationToken)
    {
        var images = new ConcurrentDictionary<string, string?>();

        await Parallel.ForEachAsync(
            attractions,
            new ParallelOptions { MaxDegreeOfParallelism = ImageFetchConcurrency, CancellationToken = cancellationToken },
            async (attraction, ct) =>
            {
                images[attraction.ProviderId] = await GetAttractionImageAsync(attraction, ct);
            });

        return attractions
            .Select(a => a with { ImageUrl = images.GetValueOrDefault(a.ProviderId) })
            .ToList();
    }

    /// <summary>
    /// Serper first (it finds a photo for far more places than the destination
    /// provider does), then the provider's own thumbnail, then a stale cached
    /// gallery rather than blanking out photos that used to be there.
    ///
    /// ONE cache entry per place, shared by the attractions list (first photo only,
    /// via <see cref="GetAttractionImageAsync"/>) and the details carousel (up to
    /// <see cref="DetailsPhotoCount"/>) — otherwise the two would cache different
    /// "top hits" and show a different photo on the list than on the details page.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetAttractionImagesAsync(string providerId, string name, string? providerImage, CancellationToken cancellationToken)
    {
        var key = $"imgs:{providerId}";
        var stale = await _cache.TryGetAsync<IReadOnlyList<string>>(key, cancellationToken);
        if (stale is not null && _clock.GetUtcNow() - stale.FetchedAt < ImageTtl)
        {
            return stale.Value;
        }

        IReadOnlyList<string> images = [];
        var searchSucceeded = false;
        try
        {
            images = await _imageSearch.SearchImagesAsync(name, DetailsPhotoCount, cancellationToken);
            searchSucceeded = true;
        }
        catch (ExternalServiceUnavailableException)
        {
            // Serper down, timed out, or returned something unparseable — fall
            // through to the next source rather than failing the whole list/page.
        }

        if (images.Count == 0 && providerImage is not null)
        {
            images = [providerImage];
        }

        if (searchSucceeded || images.Count > 0)
        {
            // A genuine resolution — Serper answered (even with zero results,
            // which is worth caching so we don't re-pay for the same negative),
            // or the provider-image fallback found something. Safe to stamp as
            // fresh.
            await _cache.SetAsync(key, new CacheEnvelope<IReadOnlyList<string>>(images, _clock.GetUtcNow()), cancellationToken);
            return images;
        }

        // Serper threw and nothing else resolved it — serve the stale gallery
        // (if any) WITHOUT refreshing its timestamp, so the entry still reads
        // as due for a retry next time instead of looking freshly-fetched for
        // a full ImageTtl.
        return stale?.Value ?? images;
    }

    /// <summary>
    /// Single-photo view over <see cref="GetAttractionImagesAsync"/> for the
    /// attractions list, which only needs a thumbnail — shares its cache so a
    /// place already looked up on the list (or its details page) isn't queried
    /// twice.
    /// </summary>
    private async Task<string?> GetAttractionImageAsync(DestinationSummaryDto attraction, CancellationToken cancellationToken)
    {
        var images = await GetAttractionImagesAsync(attraction.ProviderId, attraction.Name, attraction.ImageUrl, cancellationToken);
        return images.FirstOrDefault();
    }

    /// <summary>
    /// Cache-aside fetch of one place's details, shared by <see cref="GetDetailsAsync"/>
    /// and the attractions-list image enrichment above — a place looked up during
    /// enrichment is already cached by the time the user clicks into its detail page.
    /// A null provider answer is deliberately NOT cached (checked by hand, not via
    /// <see cref="GetCachedAsync{T}"/>) so a transient "not found" doesn't stick.
    /// </summary>
    private async Task<DestinationDetailsDto?> GetCachedProviderDetailsAsync(string providerId, CancellationToken cancellationToken)
    {
        var key = $"details:{providerId}";
        var stale = await _cache.TryGetAsync<DestinationDetailsDto>(key, cancellationToken);
        if (stale is not null && _clock.GetUtcNow() - stale.FetchedAt < DetailsTtl)
        {
            return stale.Value;
        }

        DestinationDetailsDto? details;
        try
        {
            details = await _provider.GetDestinationDetailsAsync(providerId, cancellationToken);
        }
        catch (ExternalServiceUnavailableException)
        {
            return stale?.Value; // stale-better-than-down (spec §11.2), else null
        }

        if (details is not null)
        {
            await _cache.SetAsync(key, new CacheEnvelope<DestinationDetailsDto>(details, _clock.GetUtcNow()), cancellationToken);
        }

        return details;
    }

    public async Task<DestinationDetailsDto> GetDetailsAsync(string providerId, CancellationToken cancellationToken = default)
    {
        await DetailsValidator.ValidateAndThrowAppExceptionAsync(
            new GetDestinationDetailsRequest(providerId), cancellationToken);

        // F2/US1 (spec §11.3): the provider has the freshest data, but it can
        // say "no such place" (null) or be unreachable (falls back to stale
        // cache inside the helper) — either way, a final null falls through to
        // our own DB (DestinationService never writes; only
        // TripService.AddDestinationAsync upserts).
        var details = await GetCachedProviderDetailsAsync(providerId, cancellationToken);
        if (details is not null)
        {
            // F2/US2 wants a gallery here, not one thumbnail. Passing details.ImageUrl
            // in means the helper's own fallback covers the "Serper found nothing" case.
            var images = await GetAttractionImagesAsync(details.ProviderId, details.Name, details.ImageUrl, cancellationToken);
            return details with { ImageUrl = images.FirstOrDefault(), ImageUrls = images };
        }

        // Saved-trip destinations must stay viewable even if the provider forgets
        // them (§8.4's snapshot rationale) — only a miss on BOTH sources is 404.
        var cached = await _destinations.GetByProviderIdReadOnlyAsync(providerId, cancellationToken);

        return cached?.ToDetailsDto() ?? throw new NotFoundException(nameof(Destination), providerId);
    }
}
