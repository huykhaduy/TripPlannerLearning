using Microsoft.Extensions.Configuration;
using Moq;
using TripPlanner.Infrastructure;
using Xunit;

namespace TripPlanner.Application.Tests.Infrastructure;

/// <summary>
/// This single string is what every verification email's link is built from, so a
/// stray trailing slash produces "http://localhost:5173//verify-email" in real user
/// mail, and a missing fallback produces "/verify-email" with no host at all.
/// </summary>
public class AppUrlProviderTests
{
    private static AppUrlProvider CreateSut(string? configured)
    {
        var configuration = new Mock<IConfiguration>();
        configuration.Setup(c => c["App:FrontendBaseUrl"]).Returns(configured);
        return new AppUrlProvider(configuration.Object);
    }

    [Fact]
    public void ItUsesTheConfiguredFrontendUrl()
    {
        var sut = CreateSut("https://tripplanner.example.com");

        Assert.Equal("https://tripplanner.example.com", sut.FrontendBaseUrl);
    }

    [Fact]
    public void WithNothingConfigured_ItFallsBackToTheViteDevServer()
    {
        // Unlike Jwt:Key, a wrong URL only breaks one feature rather than security,
        // so this one falls back instead of refusing to start.
        var sut = CreateSut(null);

        Assert.Equal("http://localhost:5173", sut.FrontendBaseUrl);
    }

    [Fact]
    public void ItStripsATrailingSlash()
    {
        // Callers append "/verify-email?token=…", so keeping the slash would yield a
        // double-slashed link in the email that goes out to a real person.
        var sut = CreateSut("https://tripplanner.example.com/");

        Assert.Equal("https://tripplanner.example.com", sut.FrontendBaseUrl);
    }

    [Fact]
    public void ItStripsSeveralTrailingSlashes()
    {
        var sut = CreateSut("https://tripplanner.example.com///");

        Assert.Equal("https://tripplanner.example.com", sut.FrontendBaseUrl);
    }

    [Fact]
    public void ItKeepsAConfiguredSubPath()
    {
        // Only the trailing separator goes; a base path is part of the address.
        var sut = CreateSut("https://example.com/tripplanner/");

        Assert.Equal("https://example.com/tripplanner", sut.FrontendBaseUrl);
    }

    [Fact]
    public void TheValueIsReadOnceAtConstruction()
    {
        // FrontendBaseUrl is a get-only property set in the constructor, and the
        // provider is registered as a singleton — so configuration is read exactly
        // once rather than on every verification email.
        var configuration = new Mock<IConfiguration>();
        configuration.Setup(c => c["App:FrontendBaseUrl"]).Returns("https://first.example.com");
        var sut = new AppUrlProvider(configuration.Object);

        _ = sut.FrontendBaseUrl;
        _ = sut.FrontendBaseUrl;

        configuration.Verify(c => c["App:FrontendBaseUrl"], Times.Once);
    }
}
