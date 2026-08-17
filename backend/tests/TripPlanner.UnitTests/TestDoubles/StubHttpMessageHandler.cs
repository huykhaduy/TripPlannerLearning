using System.Net;
using System.Text;

namespace TripPlanner.UnitTests.TestDoubles;

/// <summary>
/// Answers every request from a caller-supplied function, recording what was asked.
///
/// The typed clients (<c>GeoapifyClient</c>, <c>SerperImageClient</c>) are tested
/// through a real <see cref="HttpClient"/> over this handler rather than by mocking
/// HttpClient itself, because the parts worth pinning — the request URL Geoapify
/// actually needs (lon before lat, radius in metres), the status codes that mean
/// "no such place" versus "a real fault", and the JSON shape being parsed — all
/// live at the HTTP boundary and would be assumed away by a higher mock.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        _respond = respond;

    /// <summary>Replies to every request with <paramref name="json"/> at <paramref name="status"/>.</summary>
    public static StubHttpMessageHandler Returning(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    /// <summary>Replies with <paramref name="status"/> and no body.</summary>
    public static StubHttpMessageHandler ReturningStatus(HttpStatusCode status) =>
        new(_ => new HttpResponseMessage(status));

    /// <summary>Fails every request the way a dead network does.</summary>
    public static StubHttpMessageHandler Throwing(Exception exception) =>
        new(_ => throw exception);

    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>The absolute URI of the single request made, for URL assertions.</summary>
    public Uri LastRequestUri => Requests[^1].RequestUri!;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_respond(request));
    }

    /// <summary>An <see cref="HttpClient"/> over this handler, pointed at a dummy base address.</summary>
    public HttpClient CreateClient(string baseAddress = "https://provider.test/") =>
        new(this) { BaseAddress = new Uri(baseAddress) };
}
