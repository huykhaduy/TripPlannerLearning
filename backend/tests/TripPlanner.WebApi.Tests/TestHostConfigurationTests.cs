using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TripPlanner.Infrastructure.Identity;
using Xunit;

namespace TripPlanner.WebApi.Tests;

/// <summary>
/// Pins the test host's configuration. Program.cs calls DotNetEnv.Env.Load(), so a
/// developer with a real backend/src/TripPlanner.WebApi/.env has Jwt__Key set as a
/// process environment variable while the suite runs. Without this test, the factory's
/// own value could be silently overridden and the tests would sign tokens with that
/// developer's real key — passing locally, and passing on CI, for different reasons.
/// </summary>
public class TestHostConfigurationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public TestHostConfigurationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void TestHost_UsesTheFactorysJwtKey_NotTheLocalEnvFile()
    {
        using var scope = _factory.Services.CreateScope();

        var jwt = scope.ServiceProvider.GetRequiredService<IOptions<JwtSettings>>().Value;

        Assert.Equal(CustomWebApplicationFactory.TestJwtKey, jwt.Key);
    }
}
