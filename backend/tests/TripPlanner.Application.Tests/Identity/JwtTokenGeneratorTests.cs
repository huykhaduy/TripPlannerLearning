using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TripPlanner.Domain.Entities;
using TripPlanner.Infrastructure.Identity;
using Xunit;

namespace TripPlanner.Application.Tests.Identity;

/// <summary>
/// Unit tests for the REAL <see cref="JwtTokenGenerator"/> (AuthServiceTests
/// mocks <c>IJwtTokenGenerator</c> entirely, so the actual signing/parsing
/// logic — especially the F4/US2 email-verification token's purpose claim —
/// needs its own coverage).
/// </summary>
public class JwtTokenGeneratorTests
{
    private static JwtTokenGenerator CreateSut() =>
        new(Options.Create(new JwtSettings
        {
            Issuer = "TripPlanner",
            Audience = "TripPlannerClient",
            Key = "unit-test-signing-key-min-32-chars-long!",
            ExpiryMinutes = 60,
        }));

    private static User CreateUser() => new()
    {
        Email = "user@example.com",
        PasswordHash = "irrelevant",
    };

    [Fact]
    public void EmailVerificationToken_RoundTripsToTheSameUserId()
    {
        var sut = CreateSut();
        var user = CreateUser();

        var token = sut.GenerateEmailVerificationToken(user);
        var result = sut.ValidateEmailVerificationToken(token);

        Assert.Equal(user.Id, result);
    }

    [Fact]
    public void ValidateEmailVerificationToken_WithGarbage_ReturnsNull()
    {
        var sut = CreateSut();

        Assert.Null(sut.ValidateEmailVerificationToken("not-a-real-jwt"));
    }

    [Fact]
    public void ValidateEmailVerificationToken_RejectsANormalAccessToken()
    {
        // A regular access token is signed with the same key but carries no
        // "purpose" claim — it must not be usable to verify an email.
        var sut = CreateSut();
        var (accessToken, _) = sut.GenerateToken(CreateUser());

        Assert.Null(sut.ValidateEmailVerificationToken(accessToken));
    }

    [Fact]
    public void EmailVerificationToken_FailsTheWebApisNormalAccessTokenValidation()
    {
        // Regression test for a real vulnerability: the verification token
        // must be rejected by the SAME TokenValidationParameters shape the
        // Web API's AddJwtBearer(...) uses in Program.cs to guard every
        // [Authorize] endpoint — otherwise a leaked verification link would
        // double as a 24h Bearer credential for the account. Program.cs only
        // ever supplies ValidAudience = the normal access-token audience, so
        // this must throw rather than validate.
        var sut = CreateSut();
        var verificationToken = sut.GenerateEmailVerificationToken(CreateUser());

        var accessTokenPipelineParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "TripPlanner",
            ValidateAudience = true,
            ValidAudience = "TripPlannerClient", // matches CreateSut's JwtSettings.Audience
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("unit-test-signing-key-min-32-chars-long!")),
            ClockSkew = TimeSpan.Zero,
        };

        Assert.Throws<SecurityTokenInvalidAudienceException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(verificationToken, accessTokenPipelineParameters, out _));
    }
}
