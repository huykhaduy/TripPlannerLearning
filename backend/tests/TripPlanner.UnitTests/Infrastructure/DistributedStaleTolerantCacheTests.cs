using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TripPlanner.Application.Common.Caching;
using TripPlanner.Infrastructure.Caching;
using TripPlanner.UnitTests.TestDoubles;
using Xunit;

namespace TripPlanner.UnitTests.Infrastructure;

/// <summary>
/// The JSON/bytes half of the browse-path cache, moved out of
/// <c>DestinationService</c> so the Application layer keeps only the caching
/// POLICY (which TTL, when to serve stale) and none of the mechanism.
///
/// The "entry no longer matches its type" path had no test at all while it lived
/// in Application — the service was only ever handed a NullLogger, so the Warning
/// that is its entire visible behaviour went unasserted.
/// </summary>
public class DistributedStaleTolerantCacheTests
{
    private sealed record Sample(string Name, int Count);

    private static IDistributedCache NewBackend() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private static DistributedStaleTolerantCache CreateSut(
        IDistributedCache? backend = null,
        ILogger<DistributedStaleTolerantCache>? logger = null) =>
        new(backend ?? NewBackend(), logger ?? new RecordingLogger<DistributedStaleTolerantCache>());

    [Fact]
    public async Task TryGetAsync_OnAMiss_ReturnsNull()
    {
        var sut = CreateSut();

        Assert.Null(await sut.TryGetAsync<Sample>("absent"));
    }

    [Fact]
    public async Task SetAsync_ThenTryGetAsync_RoundTripsTheValueAndItsTimestamp()
    {
        var backend = NewBackend();
        var sut = CreateSut(backend);
        var fetchedAt = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

        await sut.SetAsync("k", new CacheEnvelope<Sample>(new Sample("Hanoi", 3), fetchedAt));
        var result = await sut.TryGetAsync<Sample>("k");

        Assert.NotNull(result);
        Assert.Equal(new Sample("Hanoi", 3), result.Value);
        // The timestamp is the whole point: freshness is judged by the caller
        // against its own TTL, not by letting the backend expire the entry.
        Assert.Equal(fetchedAt, result.FetchedAt);
    }

    [Fact]
    public async Task SetAsync_ThenTryGetAsync_RoundTripsACollection()
    {
        // Every real caller stores IReadOnlyList<T>, not a bare object.
        var backend = NewBackend();
        var sut = CreateSut(backend);

        await sut.SetAsync("k", new CacheEnvelope<IReadOnlyList<string>>(["a", "b"], DateTimeOffset.UnixEpoch));
        var result = await sut.TryGetAsync<IReadOnlyList<string>>("k");

        Assert.NotNull(result);
        Assert.Equal(["a", "b"], result.Value);
    }

    [Fact]
    public async Task TryGetAsync_WhenTheEntryNoLongerMatchesTheType_IsTreatedAsAMiss()
    {
        // Entries outlive a DTO reshape (retention is 7 days), so a stale entry in
        // the old shape must degrade to a miss rather than 500 the request.
        var backend = NewBackend();
        await backend.SetAsync("k", "{ this is not json at all "u8.ToArray());
        var sut = CreateSut(backend);

        Assert.Null(await sut.TryGetAsync<Sample>("k"));
    }

    [Fact]
    public async Task TryGetAsync_WhenTheEntryNoLongerMatchesTheType_LogsAWarning()
    {
        // Degrading silently is correct; degrading INVISIBLY is not — without this
        // line a week of discarded entries looks like "the cache stopped working".
        var backend = NewBackend();
        await backend.SetAsync("k", "{ this is not json at all "u8.ToArray());
        var logger = new RecordingLogger<DistributedStaleTolerantCache>();
        var sut = CreateSut(backend, logger);

        await sut.TryGetAsync<Sample>("k");

        var warning = Assert.Single(logger.Warnings);
        Assert.Contains(nameof(Sample), warning.Message);
        Assert.NotNull(warning.Exception);
    }

    [Fact]
    public async Task TryGetAsync_OnAHit_LogsNothing()
    {
        var backend = NewBackend();
        var logger = new RecordingLogger<DistributedStaleTolerantCache>();
        var sut = CreateSut(backend, logger);

        await sut.SetAsync("k", new CacheEnvelope<Sample>(new Sample("Hanoi", 3), DateTimeOffset.UnixEpoch));
        await sut.TryGetAsync<Sample>("k");

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task SetAsync_RetainsEntriesWellPastAnyTtl()
    {
        // Retention is what MAKES stale-better-than-down possible: if entries expired
        // at their TTL there would be nothing left to serve during an outage.
        var backend = new Mock<IDistributedCache>();
        DistributedCacheEntryOptions? captured = null;
        backend
            .Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (_, _, options, _) => captured = options)
            .Returns(Task.CompletedTask);
        var sut = CreateSut(backend.Object);

        await sut.SetAsync("k", new CacheEnvelope<Sample>(new Sample("Hanoi", 3), DateTimeOffset.UnixEpoch));

        Assert.Equal(TimeSpan.FromDays(7), captured?.AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public async Task SetAsync_WhenTheValueCannotBeSerialized_Throws()
    {
        // Deliberately NOT swallowed like the read side: a serialize failure is a bug
        // in our own contract, and hiding it once made a provider outage the suspect.
        var sut = CreateSut();

        await Assert.ThrowsAnyAsync<NotSupportedException>(
            () => sut.SetAsync("k", new CacheEnvelope<Type>(typeof(string), DateTimeOffset.UnixEpoch)));
    }
}
