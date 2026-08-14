using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Application.Common.Interfaces;
using TripPlanner.WebApi.Services;
using Xunit;

namespace TripPlanner.WebApi.Tests;

/// <summary>
/// Every trip operation starts by asking this class who is calling, and then filters
/// by the answer. The endpoint tests prove the happy path end to end; these cover the
/// edges a real request cannot easily produce — no HttpContext at all, an unparseable
/// subject claim, or a token carrying the raw "sub" name instead of the mapped one.
/// </summary>
public class CurrentUserServiceTests
{
    private static CurrentUserService CreateSut(ClaimsPrincipal? user)
    {
        var accessor = new HttpContextAccessor();
        if (user is not null)
        {
            accessor.HttpContext = new DefaultHttpContext { User = user };
        }
        return new CurrentUserService(accessor);
    }

    private static ClaimsPrincipal PrincipalWith(string claimType, string value) =>
        new(new ClaimsIdentity([new Claim(claimType, value)], authenticationType: "Test"));

    [Fact]
    public void UserId_ReadsTheNameIdentifierClaim()
    {
        var id = Guid.NewGuid();
        var sut = CreateSut(PrincipalWith(ClaimTypes.NameIdentifier, id.ToString()));

        Assert.Equal(id, sut.UserId);
    }

    [Fact]
    public void UserId_AlsoAcceptsARawSubClaim()
    {
        // JwtBearer maps "sub" to NameIdentifier by default, but that mapping can be
        // switched off — the fallback is what keeps this working either way.
        var id = Guid.NewGuid();
        var sut = CreateSut(PrincipalWith("sub", id.ToString()));

        Assert.Equal(id, sut.UserId);
    }

    [Fact]
    public void UserId_PrefersNameIdentifierWhenBothArePresent()
    {
        var mapped = Guid.NewGuid();
        var raw = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, mapped.ToString()), new Claim("sub", raw.ToString())],
            authenticationType: "Test"));
        var sut = CreateSut(principal);

        Assert.Equal(mapped, sut.UserId);
    }

    [Fact]
    public void UserId_WithNoHttpContext_IsNull()
    {
        // Background work and unit-tested code paths run with no request in flight.
        // Throwing here would turn "not signed in" into a 500.
        var sut = CreateSut(null);

        Assert.Null(sut.UserId);
    }

    [Fact]
    public void UserId_ForAnAnonymousRequest_IsNull()
    {
        var sut = CreateSut(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Null(sut.UserId);
    }

    [Fact]
    public void UserId_WithASubjectThatIsNotAGuid_IsNull()
    {
        // A token from another issuer might carry a numeric or opaque subject.
        // TryParse means that reads as "no user" rather than throwing FormatException
        // out of a property getter.
        var sut = CreateSut(PrincipalWith(ClaimTypes.NameIdentifier, "not-a-guid"));

        Assert.Null(sut.UserId);
    }

    [Fact]
    public void UserId_WithAnEmptySubject_IsNull()
    {
        var sut = CreateSut(PrincipalWith(ClaimTypes.NameIdentifier, ""));

        Assert.Null(sut.UserId);
    }

    [Fact]
    public void GetRequiredUserId_ReturnsTheId()
    {
        var id = Guid.NewGuid();
        var sut = CreateSut(PrincipalWith(ClaimTypes.NameIdentifier, id.ToString()));

        Assert.Equal(id, sut.GetRequiredUserId());
    }

    [Fact]
    public void GetRequiredUserId_WhenAnonymous_ThrowsUnauthorized()
    {
        // This is what makes an unauthenticated call to a trip operation a 401 rather
        // than a NullReferenceException somewhere further in.
        var sut = CreateSut(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Throws<UnauthorizedException>(() => sut.GetRequiredUserId());
    }

    [Fact]
    public void GetRequiredUserId_WithAnUnparseableSubject_AlsoThrowsUnauthorized()
    {
        // Same outcome as anonymous, deliberately: a malformed token is not a
        // half-authenticated caller.
        var sut = CreateSut(PrincipalWith(ClaimTypes.NameIdentifier, "not-a-guid"));

        Assert.Throws<UnauthorizedException>(() => sut.GetRequiredUserId());
    }
}
