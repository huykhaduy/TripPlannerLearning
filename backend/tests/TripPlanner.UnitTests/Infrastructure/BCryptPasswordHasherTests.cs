using TripPlanner.Infrastructure.Identity;
using Xunit;

namespace TripPlanner.UnitTests.Infrastructure;

/// <summary>
/// The hasher is three lines over a library, but it is the only thing standing
/// between a database dump and every user's password — so the properties it relies
/// on (salted, non-reversible, verifiable) are worth stating explicitly rather than
/// trusting to the library name.
/// </summary>
public class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _sut = new();

    [Fact]
    public void Hash_DoesNotReturnThePasswordItself()
    {
        var hash = _sut.Hash("correct horse battery staple");

        Assert.NotEqual("correct horse battery staple", hash);
        Assert.DoesNotContain("correct horse", hash);
    }

    [Fact]
    public void Hash_ProducesADifferentHashEachTimeForTheSamePassword()
    {
        // BCrypt generates a fresh salt per call. Identical hashes would mean two
        // users with the same password are visibly identical in a stolen dump.
        var first = _sut.Hash("same password");
        var second = _sut.Hash("same password");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Verify_AcceptsTheOriginalPassword()
    {
        // Which is only possible because the salt is embedded in the hash string —
        // this is what lets User store a single column instead of hash + salt.
        var hash = _sut.Hash("correct horse battery staple");

        Assert.True(_sut.Verify("correct horse battery staple", hash));
    }

    [Fact]
    public void Verify_AcceptsAPasswordAgainstADifferentlySaltedHashOfItself()
    {
        var hash = _sut.Hash("same password");
        var otherHash = _sut.Hash("same password");

        Assert.True(_sut.Verify("same password", hash));
        Assert.True(_sut.Verify("same password", otherHash));
    }

    [Fact]
    public void Verify_RejectsTheWrongPassword()
    {
        var hash = _sut.Hash("correct horse battery staple");

        Assert.False(_sut.Verify("Correct horse battery staple", hash));
        Assert.False(_sut.Verify("wrong", hash));
        Assert.False(_sut.Verify("", hash));
    }

    [Fact]
    public void Verify_IsCaseSensitive()
    {
        var hash = _sut.Hash("PassWord");

        Assert.False(_sut.Verify("password", hash));
        Assert.True(_sut.Verify("PassWord", hash));
    }

    [Fact]
    public void Hash_HandlesNonAsciiPasswords()
    {
        // The password is UTF-8 before it is hashed; a byte-vs-char slip would let
        // an account be created and then never be able to log in again.
        const string password = "mật khẩu rất dài 🔐";

        var hash = _sut.Hash(password);

        Assert.True(_sut.Verify(password, hash));
    }

    [Fact]
    public void Hash_ProducesTheStandardBCryptFormat()
    {
        var hash = _sut.Hash("anything");

        // "$2a$" / "$2b$" prefix and a fixed 60-character length — asserted so that
        // swapping the algorithm becomes a deliberate, visible change (existing
        // stored hashes would stop verifying).
        Assert.StartsWith("$2", hash);
        Assert.Equal(60, hash.Length);
    }
}
