using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TripPlanner.Application.Tests.TestDoubles;
using TripPlanner.Infrastructure.Caching;
using Xunit;

namespace TripPlanner.Application.Tests.Infrastructure;

/// <summary>
/// The decorator's whole reason to exist is which failures it swallows and which it
/// lets through. Degrading to a cache miss when Redis is unreachable is its contract
/// (the one place in the solution allowed to swallow); degrading on a bug in our own
/// serialization would silently turn a real fault into "the cache just never hits",
/// so everything else has to keep propagating.
/// </summary>
public class ResilientDistributedCacheTests
{
    private static readonly byte[] Value = Encoding.UTF8.GetBytes("cached");

    private static ResilientDistributedCache CreateSut(IDistributedCache inner, ILogger<ResilientDistributedCache> logger) =>
        new(inner, logger);

    private static IDistributedCache RealCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    /// <summary>An inner cache whose every operation fails the same way.</summary>
    private sealed class FailingCache(Exception failure) : IDistributedCache
    {
        public byte[]? Get(string key) => throw failure;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw failure;
        public void Refresh(string key) => throw failure;
        public Task RefreshAsync(string key, CancellationToken token = default) => throw failure;
        public void Remove(string key) => throw failure;
        public Task RemoveAsync(string key, CancellationToken token = default) => throw failure;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw failure;
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw failure;
    }

    private static RedisConnectionException RedisDown() =>
        new(ConnectionFailureType.UnableToConnect, "No connection is available.");

    // ------------------------------------------------------------------
    // Pass-through when the backend is healthy
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_WhenTheBackendIsHealthy_ReturnsWhatWasStored()
    {
        var logger = new RecordingLogger<ResilientDistributedCache>();
        var sut = CreateSut(RealCache(), logger);

        await sut.SetAsync("loc:hanoi", Value, new DistributedCacheEntryOptions());
        var result = await sut.GetAsync("loc:hanoi");

        Assert.Equal(Value, result);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task RemoveAsync_WhenTheBackendIsHealthy_ActuallyRemoves()
    {
        var sut = CreateSut(RealCache(), new RecordingLogger<ResilientDistributedCache>());
        await sut.SetAsync("loc:hanoi", Value, new DistributedCacheEntryOptions());

        await sut.RemoveAsync("loc:hanoi");

        Assert.Null(await sut.GetAsync("loc:hanoi"));
    }

    [Fact]
    public void Get_WhenTheBackendIsHealthy_ReturnsWhatWasStored()
    {
        // The synchronous half of IDistributedCache goes through a separate Guard
        // overload, so it needs its own coverage rather than riding on the async one.
        var sut = CreateSut(RealCache(), new RecordingLogger<ResilientDistributedCache>());

        sut.Set("loc:hanoi", Value, new DistributedCacheEntryOptions());

        Assert.Equal(Value, sut.Get("loc:hanoi"));
    }

    // ------------------------------------------------------------------
    // Degrading when the backend is unavailable
    // ------------------------------------------------------------------

    public static TheoryData<Exception> UnavailableFailures() =>
    [
        new RedisConnectionException(ConnectionFailureType.UnableToConnect, "No connection is available."),
        new RedisTimeoutException("Timeout awaiting response", CommandStatus.Sent),
        new TimeoutException("The operation has timed out."),
    ];

    [Theory]
    [MemberData(nameof(UnavailableFailures))]
    public async Task GetAsync_WhenTheCacheIsUnavailable_ReadsAsAMiss(Exception failure)
    {
        var sut = CreateSut(new FailingCache(failure), new RecordingLogger<ResilientDistributedCache>());

        var result = await sut.GetAsync("loc:hanoi");

        // Null is "not cached", which is exactly what DestinationService already
        // knows how to handle — it re-fetches from the provider.
        Assert.Null(result);
    }

    [Theory]
    [MemberData(nameof(UnavailableFailures))]
    public async Task SetAsync_WhenTheCacheIsUnavailable_IsANoOpRatherThanAFailedRequest(Exception failure)
    {
        var sut = CreateSut(new FailingCache(failure), new RecordingLogger<ResilientDistributedCache>());

        // No assertion beyond "did not throw" is possible here, and that is the
        // point: a write that cannot reach Redis must not fail the user's search.
        await sut.SetAsync("loc:hanoi", Value, new DistributedCacheEntryOptions());
    }

    [Fact]
    public async Task RefreshAsync_AndRemoveAsync_AlsoDegrade()
    {
        var sut = CreateSut(new FailingCache(RedisDown()), new RecordingLogger<ResilientDistributedCache>());

        await sut.RefreshAsync("loc:hanoi");
        await sut.RemoveAsync("loc:hanoi");
    }

    [Fact]
    public void TheSynchronousMembers_AlsoDegrade()
    {
        var sut = CreateSut(new FailingCache(RedisDown()), new RecordingLogger<ResilientDistributedCache>());

        Assert.Null(sut.Get("loc:hanoi"));
        sut.Set("loc:hanoi", Value, new DistributedCacheEntryOptions());
        sut.Refresh("loc:hanoi");
        sut.Remove("loc:hanoi");
    }

    // ------------------------------------------------------------------
    // Silent, but not invisible
    // ------------------------------------------------------------------

    [Fact]
    public async Task WhenItSwallowsAFailure_ItLogsAWarningNamingTheOperation()
    {
        // Without this, a Redis outage is indistinguishable from a cache that
        // simply never gets a hit — the failure would leave no trace at all.
        var logger = new RecordingLogger<ResilientDistributedCache>();
        var failure = RedisDown();
        var sut = CreateSut(new FailingCache(failure), logger);

        await sut.GetAsync("loc:hanoi");

        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("GetAsync", warning.Message);
        Assert.Same(failure, warning.Exception);
    }

    [Fact]
    public async Task ItDoesNotLogTheCacheKey()
    {
        // Cache keys embed the user's raw search term ("loc:{query}"), so logging
        // one would put user input into whatever sink is configured.
        var logger = new RecordingLogger<ResilientDistributedCache>();
        var sut = CreateSut(new FailingCache(RedisDown()), logger);

        await sut.GetAsync("loc:someone's private search");

        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("someone's private search"));
    }

    [Fact]
    public async Task EachSwallowedFailureIsLogged_NotJustTheFirst()
    {
        var logger = new RecordingLogger<ResilientDistributedCache>();
        var sut = CreateSut(new FailingCache(RedisDown()), logger);

        await sut.GetAsync("a");
        await sut.GetAsync("b");
        await sut.SetAsync("c", Value, new DistributedCacheEntryOptions());

        Assert.Equal(3, logger.Warnings.Count());
    }

    // ------------------------------------------------------------------
    // What must NOT be swallowed
    // ------------------------------------------------------------------

    [Fact]
    public async Task AnUnrelatedFailureIsNotSwallowed()
    {
        // Only connectivity counts as "degrade". A NullReferenceException from our
        // own code is a bug, and hiding it behind a cache miss would make the app
        // look merely slow while it silently stopped caching anything.
        var logger = new RecordingLogger<ResilientDistributedCache>();
        var sut = CreateSut(new FailingCache(new NullReferenceException()), logger);

        await Assert.ThrowsAsync<NullReferenceException>(() => sut.GetAsync("loc:hanoi"));
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task AnInvalidOperationFromTheBackendIsNotSwallowed()
    {
        var sut = CreateSut(
            new FailingCache(new InvalidOperationException("serializer misconfigured")),
            new RecordingLogger<ResilientDistributedCache>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SetAsync("loc:hanoi", Value, new DistributedCacheEntryOptions()));
    }
}
