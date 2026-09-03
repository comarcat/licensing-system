using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LicensingCore.Crypto;
using LicensingCore.Entities;
using Xunit;

namespace LicensingSystem.Tests;

public class LicenseSignerTests
{
    private readonly RSA _rsa = RSA.Create(2048);

    /// <summary>A well-formed license that carries a subscription expiry.</summary>
    private static License NewLicenseWithExpiry() => new()
    {
        LicenseKey = "ABCD-EFGHJ-KLMN-PQRS-TUVW-XYZ2-34",
        ProductId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        ModelSnapshot = LicenseModel.Machine | LicenseModel.Subscription,
        MaxActivations = 7,
        SubscriptionExpiryUtc = new DateTime(2027, 6, 1, 12, 30, 45, DateTimeKind.Utc),
        Signature = Array.Empty<byte>(),
    };

    /// <summary>Same license shape but without a subscription expiry.</summary>
    private static License NewLicenseWithoutExpiry()
    {
        var license = NewLicenseWithExpiry();
        license.ModelSnapshot = LicenseModel.Machine;
        license.SubscriptionExpiryUtc = null;
        return license;
    }

    /// <summary>A public-only RSA instance derived from the shared signing key.</summary>
    private RSA MatchingPublicKey()
    {
        var pub = RSA.Create();
        pub.ImportParameters(_rsa.ExportParameters(includePrivateParameters: false));
        return pub;
    }

    // ---------------------------------------------------------------------
    // Acceptance criterion 1 — round-trip with the matching key.
    // ---------------------------------------------------------------------

    [Fact]
    public void SignThenVerify_WithMatchingKey_ReturnsTrue()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithExpiry();

        var signature = signer.Sign(license);

        Assert.True(signer.Verify(license, signature, MatchingPublicKey()));
    }

    [Fact]
    public void SignThenVerify_NoExpiry_RoundTrips()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithoutExpiry();

        var signature = signer.Sign(license);

        Assert.True(signer.Verify(license, signature, MatchingPublicKey()));
    }

    // ---------------------------------------------------------------------
    // Acceptance criterion 2 — mutating any canonical field breaks Verify.
    // ---------------------------------------------------------------------

    [Fact]
    public void Verify_AfterMutatingAnyCanonicalField_ReturnsFalse()
    {
        var signer = new LicenseSigner(_rsa);
        var pub = MatchingPublicKey();

        // LicenseKey
        var byKey = NewLicenseWithExpiry();
        var keySig = signer.Sign(byKey);
        byKey.LicenseKey = "ZZZZ-ZZZZZ-ZZZZ-ZZZZ-ZZZZ-ZZZZ-99";
        Assert.False(signer.Verify(byKey, keySig, pub));

        // ProductId
        var byProduct = NewLicenseWithExpiry();
        var productSig = signer.Sign(byProduct);
        byProduct.ProductId = Guid.NewGuid();
        Assert.False(signer.Verify(byProduct, productSig, pub));

        // ModelSnapshot
        var byModel = NewLicenseWithExpiry();
        var modelSig = signer.Sign(byModel);
        byModel.ModelSnapshot = LicenseModel.Floating;
        Assert.False(signer.Verify(byModel, modelSig, pub));

        // MaxActivations
        var byMax = NewLicenseWithExpiry();
        var maxSig = signer.Sign(byMax);
        byMax.MaxActivations += 1;
        Assert.False(signer.Verify(byMax, maxSig, pub));

        // SubscriptionExpiryUtc
        var byExpiry = NewLicenseWithExpiry();
        var expirySig = signer.Sign(byExpiry);
        byExpiry.SubscriptionExpiryUtc = byExpiry.SubscriptionExpiryUtc!.Value.AddDays(1);
        Assert.False(signer.Verify(byExpiry, expirySig, pub));
    }

    // ---------------------------------------------------------------------
    // Acceptance criterion 3 — Verify input hardening (M-1):
    //   different key / null / zero-length / malformed signature => false, no throw;
    //   null license / null publicKey => ArgumentNullException.
    // ---------------------------------------------------------------------

    [Fact]
    public void Verify_WithDifferentPublicKey_ReturnsFalse()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithExpiry();
        var signature = signer.Sign(license);

        using var stranger = RSA.Create(2048);
        var strangerPublic = RSA.Create();
        strangerPublic.ImportParameters(stranger.ExportParameters(includePrivateParameters: false));

        Assert.False(signer.Verify(license, signature, strangerPublic));
    }

    [Fact]
    public void Verify_WithNullSignature_ReturnsFalse()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithExpiry();

        // M-1: a null signature is a caller mistake / tampering, not an exception path.
        Assert.False(signer.Verify(license, null!, MatchingPublicKey()));
    }

    [Fact]
    public void Verify_WithEmptySignature_ReturnsFalse()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithExpiry();

        Assert.False(signer.Verify(license, Array.Empty<byte>(), MatchingPublicKey()));
    }

    [Fact]
    public void Verify_WithWrongLengthGarbageSignature_ReturnsFalse()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithExpiry();

        Assert.False(signer.Verify(license, new byte[] { 1, 2, 3, 4, 5 }, MatchingPublicKey()));
    }

    [Fact]
    public void Verify_WithCorrectLengthButInvalidSignature_ReturnsFalse()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithExpiry();

        // 2048-bit key => 256-byte signature block; all-zero is well-formed length, invalid content.
        Assert.False(signer.Verify(license, new byte[256], MatchingPublicKey()));
    }

    [Fact]
    public void Verify_WithCorrectLengthAllOnesSignature_ReturnsFalseWithoutThrowing()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithExpiry();

        var garbage = new byte[256];
        Array.Fill(garbage, (byte)0xFF);

        // Some runtimes surface this as a CryptographicException from VerifyData;
        // LicenseSigner.Verify must swallow it and return false.
        Assert.False(signer.Verify(license, garbage, MatchingPublicKey()));
    }

    [Fact]
    public void Verify_WithNullLicense_ThrowsArgumentNullException()
    {
        var signer = new LicenseSigner(_rsa);
        var signature = signer.Sign(NewLicenseWithExpiry());

        Assert.Throws<ArgumentNullException>(
            () => signer.Verify(null!, signature, MatchingPublicKey()));
    }

    [Fact]
    public void Verify_WithNullPublicKey_ThrowsArgumentNullException()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithExpiry();
        var signature = signer.Sign(license);

        Assert.Throws<ArgumentNullException>(
            () => signer.Verify(license, signature, null!));
    }

    // ---------------------------------------------------------------------
    // Acceptance criterion 4 — exact canonical string, new licsig-v1 format
    // with UTC-normalized, second-truncated expiry (yyyy-MM-ddTHH:mm:ss'Z').
    // ---------------------------------------------------------------------

    [Fact]
    public void CanonicalBytes_StartsWithDomainPrefix()
    {
        // M-2: domain separation from LicenseFileService signatures.
        var actual = Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(NewLicenseWithExpiry()));

        Assert.StartsWith("licsig-v1|", actual);
    }

    [Fact]
    public void CanonicalBytes_WithExpiry_IsExactPipeDelimitedString()
    {
        var license = NewLicenseWithExpiry();
        var expiry = license.SubscriptionExpiryUtc!.Value;
        var expected = FormattableString.Invariant(
            $"licsig-v1|{license.LicenseKey}|{license.ProductId:D}|{(int)license.ModelSnapshot}|{license.MaxActivations}|{expiry:yyyy-MM-ddTHH:mm:ss'Z'}");

        var actual = Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(license));

        Assert.Equal(expected, actual);
        Assert.Equal("licsig-v1|ABCD-EFGHJ-KLMN-PQRS-TUVW-XYZ2-34|11111111-2222-3333-4444-555555555555|9|7|2027-06-01T12:30:45Z", actual);
    }

    [Fact]
    public void CanonicalBytes_WithoutExpiry_EndsWithEmptyTrailingField()
    {
        var license = NewLicenseWithoutExpiry();
        var expected = FormattableString.Invariant(
            $"licsig-v1|{license.LicenseKey}|{license.ProductId:D}|{(int)license.ModelSnapshot}|{license.MaxActivations}|");

        var actual = Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(license));

        Assert.Equal(expected, actual);
        Assert.EndsWith("|", actual);
    }

    [Fact]
    public void CanonicalBytes_UtcExpiry_EndsWithZ()
    {
        var license = NewLicenseWithExpiry();
        license.SubscriptionExpiryUtc = new DateTime(2027, 6, 1, 12, 30, 45, DateTimeKind.Utc);

        var actual = Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(license));

        Assert.EndsWith("|2027-06-01T12:30:45Z", actual);
    }

    [Fact]
    public void CanonicalBytes_TruncatesSubSecondPrecision()
    {
        var license = NewLicenseWithExpiry();
        license.SubscriptionExpiryUtc =
            new DateTime(2027, 6, 1, 12, 30, 45, DateTimeKind.Utc).AddTicks(9_999_999);

        var actual = Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(license));

        Assert.EndsWith("|2027-06-01T12:30:45Z", actual);
    }

    [Fact]
    public void CanonicalBytes_UnspecifiedExpiry_TreatedAsUtc_NotShifted()
    {
        var utc = NewLicenseWithExpiry();
        utc.SubscriptionExpiryUtc = new DateTime(2027, 6, 1, 12, 30, 45, DateTimeKind.Utc);

        var unspecified = NewLicenseWithExpiry();
        unspecified.SubscriptionExpiryUtc =
            new DateTime(2027, 6, 1, 12, 30, 45, DateTimeKind.Unspecified);

        Assert.Equal(
            Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(utc)),
            Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(unspecified)));
    }

    [Fact]
    public void CanonicalBytes_LocalExpiry_ConvertedToUtc()
    {
        var instant = new DateTime(2027, 6, 1, 12, 30, 45, DateTimeKind.Utc);

        var utc = NewLicenseWithExpiry();
        utc.SubscriptionExpiryUtc = instant;

        var local = NewLicenseWithExpiry();
        local.SubscriptionExpiryUtc = instant.ToLocalTime();

        Assert.Equal(
            Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(utc)),
            Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(local)));
    }

    // Gives the Local branch teeth independent of the host time zone: it compares
    // the SAME wall-clock reading tagged Local vs. Unspecified. FormatExpiry must
    // convert Local via ToUniversalTime() (offset applied) but treat Unspecified
    // as already-UTC (offset NOT applied). On a host whose local offset for the
    // instant is non-zero the two canonical strings must differ, and the Local
    // one must equal the true UTC of that wall-clock; on a UTC host (e.g. CI)
    // both are correctly identical. Either way the Local result is pinned to
    // ToLocalTime()/ToUniversalTime() semantics, not to the host being non-UTC.
    [Fact]
    public void CanonicalBytes_LocalBranch_AppliesOffset_UnlikeUnspecifiedBranch()
    {
        var wall = new DateTime(2027, 6, 1, 12, 30, 45); // Kind == Unspecified
        var hostOffset = TimeZoneInfo.Local.GetUtcOffset(DateTime.SpecifyKind(wall, DateTimeKind.Local));

        var asLocal = NewLicenseWithExpiry();
        asLocal.SubscriptionExpiryUtc = DateTime.SpecifyKind(wall, DateTimeKind.Local);

        var asUnspecified = NewLicenseWithExpiry();
        asUnspecified.SubscriptionExpiryUtc = wall;

        var localCanon = Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(asLocal));
        var unspecifiedCanon = Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(asUnspecified));

        // Unspecified is never shifted.
        Assert.EndsWith("|2027-06-01T12:30:45Z", unspecifiedCanon);

        // Local is the same wall-clock converted to UTC via the host zone.
        var expectedLocalUtc = DateTime.SpecifyKind(wall, DateTimeKind.Local).ToUniversalTime();
        Assert.EndsWith("|" + expectedLocalUtc.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture), localCanon);

        if (hostOffset == TimeSpan.Zero)
        {
            Assert.Equal(unspecifiedCanon, localCanon);
        }
        else
        {
            Assert.NotEqual(unspecifiedCanon, localCanon);
        }
    }

    // ---------------------------------------------------------------------
    // Acceptance criterion 5 — same instant signed as Unspecified / Utc /
    // Local / truncated-to-seconds => identical signature and Verify == true.
    // ---------------------------------------------------------------------

    [Fact]
    public void Sign_SameInstant_AcrossKindsAndSubSecond_ProducesIdenticalSignatureAndVerifies()
    {
        var signer = new LicenseSigner(_rsa);
        var pub = MatchingPublicKey();

        // A UTC instant carrying sub-second precision that a DB round-trip would drop.
        var baseUtc = new DateTime(2027, 6, 1, 12, 30, 45, DateTimeKind.Utc).AddTicks(1_234_567);

        License WithExpiry(DateTime expiry)
        {
            var l = NewLicenseWithExpiry();
            l.SubscriptionExpiryUtc = expiry;
            return l;
        }

        var original = WithExpiry(baseUtc);
        var originalSig = signer.Sign(original);

        var variants = new[]
        {
            baseUtc,                                                   // Utc, sub-second
            DateTime.SpecifyKind(baseUtc, DateTimeKind.Unspecified),   // Unspecified (treated as UTC)
            baseUtc.ToLocalTime(),                                     // Local, same instant
            new DateTime(                                              // truncated to whole seconds
                baseUtc.Ticks - (baseUtc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc),
        };

        foreach (var v in variants)
        {
            var license = WithExpiry(v);

            // RSA PKCS#1 v1.5 is deterministic: identical canonical bytes => identical signature.
            Assert.Equal(originalSig, signer.Sign(license));
            Assert.True(signer.Verify(license, originalSig, pub));
        }

        Assert.EndsWith(
            "|2027-06-01T12:30:45Z",
            Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(original)));
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void SignThenVerify_RoundTrips_RegardlessOfExpiryKind(DateTimeKind kind)
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithExpiry();
        license.SubscriptionExpiryUtc =
            DateTime.SpecifyKind(new DateTime(2027, 6, 1, 12, 30, 45), kind);

        var signature = signer.Sign(license);

        // CanonicalBytes normalises every Kind to UTC before formatting, so the
        // in-memory round-trip holds for all three Kinds.
        Assert.True(signer.Verify(license, signature, MatchingPublicKey()));
    }

    // ---------------------------------------------------------------------
    // Robustness / edge coverage beyond the acceptance criteria.
    // All deterministic and fast.
    // ---------------------------------------------------------------------

    [Fact]
    public void Sign_IsDeterministic_ForPkcs1V15()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithExpiry();

        var first = signer.Sign(license);
        var second = signer.Sign(license);

        // RSA PKCS#1 v1.5 signatures are deterministic. (This would NOT hold for RSA-PSS.)
        Assert.Equal(first, second);
    }

    [Fact]
    public void CanonicalBytes_IsStable_AcrossRepeatedCalls()
    {
        var license = NewLicenseWithExpiry();

        Assert.Equal(LicenseSigner.CanonicalBytes(license), LicenseSigner.CanonicalBytes(license));
    }

    [Fact]
    public void SignThenVerify_WithModelSnapshotNone_RoundTripsAndCanonicalUsesZero()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithoutExpiry();
        license.ModelSnapshot = LicenseModel.None;

        var signature = signer.Sign(license);

        Assert.True(signer.Verify(license, signature, MatchingPublicKey()));
        Assert.Contains("|0|", Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(license)));
    }

    [Fact]
    public void SignThenVerify_WithAllModelFlags_RoundTripsAndCanonicalUsesBitmaskInt()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithExpiry();
        license.ModelSnapshot = LicenseModel.Machine | LicenseModel.User
            | LicenseModel.Floating | LicenseModel.Subscription;

        var signature = signer.Sign(license);

        Assert.True(signer.Verify(license, signature, MatchingPublicKey()));
        Assert.Contains("|15|", Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(license)));
    }

    [Fact]
    public void Verify_AfterAddingAModelFlagToNone_ReturnsFalse()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithoutExpiry();
        license.ModelSnapshot = LicenseModel.None;
        var signature = signer.Sign(license);

        license.ModelSnapshot = LicenseModel.Machine;

        Assert.False(signer.Verify(license, signature, MatchingPublicKey()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void SignThenVerify_WithNonPositiveMaxActivations_RoundTrips(int maxActivations)
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithoutExpiry();
        license.MaxActivations = maxActivations;

        var signature = signer.Sign(license);

        // The signer performs no domain validation; it just serialises the int.
        Assert.True(signer.Verify(license, signature, MatchingPublicKey()));
        Assert.Contains(
            "|" + maxActivations.ToString(CultureInfo.InvariantCulture) + "|",
            Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(license)));
    }

    [Fact]
    public void Verify_AfterMutatingMaxActivationsFromZeroToNegative_ReturnsFalse()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithoutExpiry();
        license.MaxActivations = 0;
        var signature = signer.Sign(license);

        license.MaxActivations = -1;

        Assert.False(signer.Verify(license, signature, MatchingPublicKey()));
    }

    [Fact]
    public void SignThenVerify_WithEmptyProductId_RoundTrips()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithoutExpiry();
        license.ProductId = Guid.Empty;

        var signature = signer.Sign(license);

        Assert.True(signer.Verify(license, signature, MatchingPublicKey()));
        Assert.Contains(
            "|00000000-0000-0000-0000-000000000000|",
            Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(license)));
    }

    [Fact]
    public void SignThenVerify_WithEmptyLicenseKey_RoundTripsAndCanonicalKeepsEmptyField()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithoutExpiry();
        license.LicenseKey = string.Empty;

        var signature = signer.Sign(license);

        Assert.True(signer.Verify(license, signature, MatchingPublicKey()));
        Assert.StartsWith("licsig-v1||", Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(license)));
    }

    [Fact]
    public void CanonicalBytes_DoesNotEscapePipeInLicenseKey()
    {
        // Documents a canonicalisation weakness: the '|' delimiter is not escaped,
        // so a LicenseKey containing '|' is ambiguous with the field layout.
        // Low severity here (all 5 fields are server-controlled at signing time and
        // the 4 trailing fields have constrained formats) but worth pinning.
        var license = NewLicenseWithoutExpiry();
        license.LicenseKey = "AAAA|1111-2222|9";

        var canonical = Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(license));

        Assert.StartsWith("licsig-v1|AAAA|1111-2222|9|", canonical);

        var signer = new LicenseSigner(_rsa);
        var signature = signer.Sign(license);
        Assert.True(signer.Verify(license, signature, MatchingPublicKey()));
    }

    [Fact]
    public void Verify_AfterSettingExpiryThatWasNull_ReturnsFalse()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithoutExpiry();
        var signature = signer.Sign(license);

        license.SubscriptionExpiryUtc = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(signer.Verify(license, signature, MatchingPublicKey()));
    }

    [Fact]
    public void Verify_AfterClearingAnExpiryThatWasSet_ReturnsFalse()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithExpiry();
        var signature = signer.Sign(license);

        license.SubscriptionExpiryUtc = null;

        Assert.False(signer.Verify(license, signature, MatchingPublicKey()));
    }

    [Fact]
    public void Verify_AcceptsAnRsaInstanceThatAlsoHoldsPrivateParameters()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithExpiry();
        var signature = signer.Sign(license);

        // Passing the full keypair (not a public-only clone) still verifies.
        Assert.True(signer.Verify(license, signature, _rsa));
    }
}
