using LicensingAdmin.Auth;
using Xunit;

namespace LicensingSystem.Tests;

public class PasswordHasherServiceTests
{
    private readonly PasswordHasherService _svc = new();

    [Fact]
    public void Hash_ThenVerify_WithSamePassword_ReturnsTrue()
    {
        const string password = "Correct horse battery staple 42!";

        var hash = _svc.Hash(password);

        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.True(_svc.Verify(hash, password));
    }

    [Fact]
    public void Verify_WithWrongPassword_ReturnsFalse()
    {
        var hash = _svc.Hash("s3cret-original");

        Assert.False(_svc.Verify(hash, "s3cret-original-WRONG"));
    }

    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentStrings()
    {
        const string password = "same-input-different-salt";

        var first = _svc.Hash(password);
        var second = _svc.Hash(password);

        Assert.NotEqual(first, second);
        Assert.True(_svc.Verify(first, password));
        Assert.True(_svc.Verify(second, password));
    }

    [Fact]
    public void Verify_WithMalformedHash_ReturnsFalseAndDoesNotThrow()
    {
        string[] garbageInputs =
        {
            "",
            "not-a-hash",
            "###",
            "AAAA", // valid Base64 but far too short to be a real hash
        };

        foreach (var garbage in garbageInputs)
        {
            var ex = Record.Exception(() => _svc.Verify(garbage, "x"));

            Assert.Null(ex);
            Assert.False(_svc.Verify(garbage, "x"));
        }
    }

    // ---------------------------------------------------------------------
    // E1-T7 tester additions: gaps around the 5 acceptance criteria.
    // Budget: PBKDF2 (100k iters) is ~tens of ms/call; each test keeps to
    // 2-4 crypto calls. No loops of thousands of hashes.
    // ---------------------------------------------------------------------

    // Criterion 1 (round-trip) — boundary: the empty password must still
    // hash and round-trip, and must not collide with a non-empty password.
    [Fact]
    public void Hash_ThenVerify_WithEmptyPassword_RoundTripsAndIsDistinctFromNonEmpty()
    {
        var hash = _svc.Hash("");

        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.True(_svc.Verify(hash, ""));
        Assert.False(_svc.Verify(hash, "x"));
    }

    // Criterion 1 (round-trip) — boundary: large password (> 1 KB).
    [Fact]
    public void Hash_ThenVerify_WithVeryLongPassword_RoundTrips()
    {
        var password = new string('a', 4096);

        var hash = _svc.Hash(password);

        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.True(_svc.Verify(hash, password));
        Assert.False(_svc.Verify(hash, password + "b"));
    }

    // Criterion 1 (round-trip) — Unicode, emoji and significant whitespace
    // must be preserved byte-for-byte: trimming the password breaks the match.
    [Fact]
    public void Hash_ThenVerify_WithUnicodeEmojiAndWhitespace_RoundTripsExactly()
    {
        const string password = "  Ünïcödé \U0001F510 パスワード  ";

        var hash = _svc.Hash(password);

        Assert.True(_svc.Verify(hash, password));
        Assert.False(_svc.Verify(hash, password.Trim()));
    }

    // Criterion 2 (wrong password) — boundary: an empty candidate password
    // against a hash of a non-empty password must be rejected.
    [Fact]
    public void Verify_WithEmptyPassword_AgainstNonEmptyPasswordHash_ReturnsFalse()
    {
        var hash = _svc.Hash("a-real-password");

        Assert.False(_svc.Verify(hash, ""));
    }

    // Criterion 4 (non-matching hash) — distinct from "malformed": a
    // perfectly VALID hash that belongs to a DIFFERENT password must return
    // false (not throw).
    [Fact]
    public void Verify_WithValidHashOfDifferentPassword_ReturnsFalseAndDoesNotThrow()
    {
        var hashOfOther = _svc.Hash("password-one");

        var ex = Record.Exception(() => _svc.Verify(hashOfOther, "password-two"));

        Assert.Null(ex);
        Assert.False(_svc.Verify(hashOfOther, "password-two"));
    }

    // Non-functional: Verify is a pure function of (hash, password) — same
    // inputs give the same verdict on repeated calls, for both hit and miss.
    [Fact]
    public void Verify_IsStable_AcrossRepeatedCalls()
    {
        var hash = _svc.Hash("stability-check");

        Assert.Equal(_svc.Verify(hash, "stability-check"), _svc.Verify(hash, "stability-check"));
        Assert.Equal(_svc.Verify(hash, "wrong"), _svc.Verify(hash, "wrong"));
        Assert.True(_svc.Verify(hash, "stability-check"));
        Assert.False(_svc.Verify(hash, "wrong"));
    }

    // Criterion 3 support / general invariant: Hash never yields null or
    // blank, and always yields a fresh string, regardless of input shape.
    [Fact]
    public void Hash_NeverReturnsNullOrEmpty_AndAlwaysDiffers()
    {
        string[] inputs = { "", "a", "   ", "\U0001F510" };

        var hashes = new HashSet<string>();
        foreach (var input in inputs)
        {
            var hash = _svc.Hash(input);
            Assert.False(string.IsNullOrWhiteSpace(hash));
            hashes.Add(hash);
        }

        Assert.Equal(inputs.Length, hashes.Count); // per-call random salt => all unique
    }

    // Criterion 4 (malformed) — extra shape: Convert.FromBase64String ignores
    // whitespace, so a whitespace-only hash decodes to zero bytes -> false,
    // no FormatException surfaces.
    [Fact]
    public void Verify_WithWhitespaceOnlyHash_ReturnsFalseAndDoesNotThrow()
    {
        var ex = Record.Exception(() => _svc.Verify("   \t  ", "x"));

        Assert.Null(ex);
        Assert.False(_svc.Verify("   \t  ", "x"));
    }

    // Documentation of REAL behavior for null arguments. The service only
    // contracts to swallow FormatException; null is a caller contract
    // violation (parameters are non-nullable) and surfaces as
    // ArgumentNullException. Recorded here so a future change is a conscious
    // decision, not an accident. Not treated as an E1-T7 gap.
    [Fact]
    public void Hash_WithNullPassword_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _svc.Hash(null!));
    }

    [Fact]
    public void Verify_WithNullHash_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _svc.Verify(null!, "x"));
    }

    [Fact]
    public void Verify_WithNullPassword_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _svc.Verify("AAAA", null!));
    }
}
