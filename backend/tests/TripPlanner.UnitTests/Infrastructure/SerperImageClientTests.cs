using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.UnitTests.TestDoubles;
using TripPlanner.Infrastructure.ExternalApis;
using Xunit;

namespace TripPlanner.UnitTests.Infrastructure;

/// <summary>
/// Serper is a paid, per-call API, so the behaviours worth pinning are mostly about
/// not spending credits: no request at all when there is no key, and a hard cap on
/// how many results a caller can ask to keep.
/// </summary>
public class SerperImageClientTests
{
    private const string ApiKey = "serper-test-key";

    private static SerperImageClient CreateSut(
        StubHttpMessageHandler handler,
        RecordingLogger<SerperImageClient>? logger = null,
        string apiKey = ApiKey) =>
        new(handler.CreateClient(),
            Options.Create(new SerperSettings { ApiKey = apiKey }),
            logger ?? new RecordingLogger<SerperImageClient>());

    private const string TwoImages = """
        {"images":[
          {"imageUrl":"https://img.test/one.jpg"},
          {"imageUrl":"https://img.test/two.jpg"}
        ]}
        """;

    [Fact]
    public async Task WithNoApiKey_ItDoesNotCallSerperAtAll()
    {
        // A fresh clone has no key. Sending the request anyway would buy a
        // guaranteed 401 and pay its latency on every attraction in every list.
        var handler = StubHttpMessageHandler.Returning(TwoImages);

        var result = await CreateSut(handler, apiKey: "").SearchImagesAsync("Imperial Citadel", 3);

        Assert.Empty(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ItPostsTheQueryToTheImagesEndpointWithTheKeyInAHeader()
    {
        var handler = StubHttpMessageHandler.Returning(TwoImages);

        await CreateSut(handler).SearchImagesAsync("Imperial Citadel", 2);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("images", request.RequestUri!.ToString());
        // Header auth, unlike Geoapify's query-string key — so this one is safe to
        // leave HttpClientFactory's request logging switched on for.
        Assert.Equal(ApiKey, Assert.Single(request.Headers.GetValues("X-API-KEY")));
    }

    [Fact]
    public async Task ItSendsTheSearchTermInTheBody()
    {
        // The body has to be read inside the handler: SerperImageClient disposes the
        // request (and with it the JsonContent) as soon as the call returns, so
        // holding on to the message and reading it afterwards throws.
        string? body = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TwoImages, System.Text.Encoding.UTF8, "application/json"),
            };
        });

        await CreateSut(handler).SearchImagesAsync("Imperial Citadel Hanoi", 1);

        Assert.Contains("Imperial Citadel Hanoi", body);
        Assert.Contains("\"q\"", body);
    }

    [Fact]
    public async Task ItReturnsTheImageUrlsInOrder()
    {
        var handler = StubHttpMessageHandler.Returning(TwoImages);

        var result = await CreateSut(handler).SearchImagesAsync("citadel", 5);

        Assert.Equal(["https://img.test/one.jpg", "https://img.test/two.jpg"], result);
    }

    [Fact]
    public async Task ItStopsAtMaxResults()
    {
        var handler = StubHttpMessageHandler.Returning(TwoImages);

        var result = await CreateSut(handler).SearchImagesAsync("citadel", 1);

        Assert.Equal("https://img.test/one.jpg", Assert.Single(result));
    }

    [Fact]
    public async Task ItDropsEntriesWithNoUrl()
    {
        var handler = StubHttpMessageHandler.Returning("""
            {"images":[{"imageUrl":null},{"imageUrl":""},{"imageUrl":"https://img.test/real.jpg"}]}
            """);

        var result = await CreateSut(handler).SearchImagesAsync("citadel", 5);

        // The cap is applied after filtering, so a page of blanks does not eat it.
        Assert.Equal("https://img.test/real.jpg", Assert.Single(result));
    }

    [Fact]
    public async Task WithNoImagesInTheBody_ItReturnsEmpty()
    {
        var handler = StubHttpMessageHandler.Returning("""{}""");

        Assert.Empty(await CreateSut(handler).SearchImagesAsync("citadel", 5));
    }

    [Fact]
    public async Task SearchImage_ReturnsTheTopHit()
    {
        var handler = StubHttpMessageHandler.Returning(TwoImages);

        var result = await CreateSut(handler).SearchImageAsync("citadel");

        Assert.Equal("https://img.test/one.jpg", result);
    }

    [Fact]
    public async Task SearchImage_WithNothingFound_ReturnsNull()
    {
        var handler = StubHttpMessageHandler.Returning("""{"images":[]}""");

        Assert.Null(await CreateSut(handler).SearchImageAsync("citadel"));
    }

    // ------------------------------------------------------------------
    // Failure handling — log at the source, then rethrow
    // ------------------------------------------------------------------

    [Fact]
    public async Task ANetworkFailureIsLoggedAndRethrown()
    {
        // Rethrowing keeps the "failed" / "found nothing" distinction alive for
        // DestinationService, which caches the second and not the first.
        var logger = new RecordingLogger<SerperImageClient>();
        var handler = StubHttpMessageHandler.Throwing(new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => CreateSut(handler, logger).SearchImagesAsync("citadel", 1));

        Assert.Contains("citadel", Assert.Single(logger.Warnings).Message);
    }

    [Fact]
    public async Task AnErrorStatusIsLoggedAndRethrown()
    {
        var logger = new RecordingLogger<SerperImageClient>();
        var handler = StubHttpMessageHandler.ReturningStatus(HttpStatusCode.TooManyRequests);

        await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => CreateSut(handler, logger).SearchImagesAsync("citadel", 1));

        Assert.Single(logger.Warnings);
    }

    [Fact]
    public async Task AnUnparseableBodyIsLoggedAndRethrown()
    {
        var logger = new RecordingLogger<SerperImageClient>();
        var handler = StubHttpMessageHandler.Returning("not json at all");

        await Assert.ThrowsAsync<ExternalServiceUnavailableException>(
            () => CreateSut(handler, logger).SearchImagesAsync("citadel", 1));

        Assert.Single(logger.Warnings);
    }

    [Fact]
    public async Task ACancelledRequestIsNotLoggedAsAProviderFailure()
    {
        var logger = new RecordingLogger<SerperImageClient>();
        var handler = StubHttpMessageHandler.Throwing(new TaskCanceledException("cancelled"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => CreateSut(handler, logger).SearchImagesAsync("citadel", 1, cts.Token));

        Assert.Empty(logger.Warnings);
    }
}
