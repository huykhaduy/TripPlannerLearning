using System.Net;
using Xunit;

namespace TripPlanner.WebApi.Tests;

/// <summary>
/// Pins the two properties of /health that the Coolify deployment depends on and
/// that nothing else in the suite would notice breaking: the exact path, and
/// anonymous access.
///
/// docker-compose.deploy.yml's healthcheck for the `api` service runs
/// `curl -f http://localhost:8080/health` from inside the container, where no JWT is
/// available. A blanket [Authorize] fallback policy, a moved route, or gating the
/// endpoint behind IsDevelopment() (as Swagger is) would each leave every other test
/// green while making every deployment report unhealthy.
///
/// The body is asserted because Coolify surfaces it in the UI: MapHealthChecks's
/// default response writer emits the status name, and with no registered checks that
/// is "Healthy".
/// </summary>
public class HealthEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public HealthEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_WithoutAToken_ReturnsHealthy()
    {
        var client = _factory.CreateClient(); // no Authorization header

        var response = await client.GetAsync("/health");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
