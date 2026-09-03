using LicensingAdmin.Auth;
using LicensingCore.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Xunit;

namespace LicensingSystem.Tests;

public class PasswordHasherServiceTests
{
    // E1-T7 M-1(a): the service takes an injected PasswordHasher<AdminUser>; in DI that
    // instance is built from IOptions<PasswordHasherOptions> so LicensingAdmin (step 8)
    // sets the iteration count. Tests build it explicitly.
    private static PasswordHasherService NewService() => new(new PasswordHasher<AdminUser>());

    private readonly PasswordHasherService _svc = NewService();

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
    // E1-T7 tester additions: gaps around the acceptance criteria.
    // Budget: PBKDF2 is ~tens of ms/call; each test keeps to 2-4 crypto
    // calls. No loops of thousands of hashes.
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

    // Criterion 4 (E1-T7 CAMBIOS / M-2): a null or empty hash, and a null
    // password, make Verify return false — it must NOT throw. Hash() is
    // unchanged and still rejects null (see Hash_WithNullPassword_... below).
    [Fact]
    public void Verify_WithNullHash_ReturnsFalse()
    {
        var ex = Record.Exception(() => _svc.Verify(null!, "x"));

        Assert.Null(ex);
        Assert.False(_svc.Verify(null!, "x"));
    }

    [Fact]
    public void Verify_WithEmptyHash_ReturnsFalse()
    {
        Assert.False(_svc.Verify("", "x"));
    }

    [Fact]
    public void Verify_WithNullPassword_ReturnsFalse()
    {
        var hash = _svc.Hash("real");
        var ex = Record.Exception(() => _svc.Verify(hash, null!));

        Assert.Null(ex);
        Assert.False(_svc.Verify(hash, null!));
    }

    [Fact]
    public void Hash_WithNullPassword_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _svc.Hash(null!));
    }

    // E1-T7 M-1(a): the hasher is injected, so LicensingAdmin can raise the
    // PBKDF2 iteration count. A service built around a hasher configured with
    // IterationCount = 210_000 (OWASP 2024 for PBKDF2-HMAC-SHA512) still
    // round-trips.
    [Fact]
    public void Constructor_UsesInjectedHasher_WithConfiguredIterationCount()
    {
        var opts = Options.Create(new PasswordHasherOptions { IterationCount = 210_000 });
        var svc = new PasswordHasherService(new PasswordHasher<AdminUser>(opts));

        var hash = svc.Hash("owasp-2024");

        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.True(svc.Verify(hash, "owasp-2024"));
        Assert.False(svc.Verify(hash, "owasp-2023"));
    }

    [Fact]
    public void Constructor_WithNullHasher_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PasswordHasherService(null!));
    }
}
