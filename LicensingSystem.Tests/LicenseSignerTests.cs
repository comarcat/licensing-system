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
    public void CanonicalBytes_WithExpiry_IsExactPipeDelimitedString()
    {
        var license = NewLicenseWithExpiry();
        var expected = FormattableString.Invariant(
            $"{license.LicenseKey}|{license.ProductId:D}|{(int)license.ModelSnapshot}|{license.MaxActivations}|{license.SubscriptionExpiryUtc:O}");

        var actual = Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(license));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CanonicalBytes_WithoutExpiry_EndsWithEmptyTrailingField()
    {
        var license = NewLicenseWithoutExpiry();
        var expected = FormattableString.Invariant(
            $"{license.LicenseKey}|{license.ProductId:D}|{(int)license.ModelSnapshot}|{license.MaxActivations}|");

        var actual = Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(license));

        Assert.Equal(expected, actual);
        Assert.EndsWith("|", actual);
    }

    // ---------------------------------------------------------------------
    // E1-T5 review additions (tester): robustness / edge coverage beyond the
    // 5 acceptance criteria. All deterministic and fast.
    // ---------------------------------------------------------------------

    // --- signature shape: malformed / empty / null -----------------------

    [Fact]
    public void Verify_WithEmptySignature_ReturnsFalse()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithExpiry();

        // RSA.VerifyData returns false (does not throw) for a non-null but
        // structurally invalid signature.
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
    public void Verify_WithNullSignature_Throws()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithExpiry();

        // Documented behaviour: LicenseSigner.Verify is a thin wrapper over
        // RSA.VerifyData, which throws ArgumentNullException on a null signature
        // rather than returning false. Callers must not pass null.
        Assert.Throws<ArgumentNullException>(
            () => signer.Verify(license, null!, MatchingPublicKey()));
    }

    // --- determinism / stability ---------------------------------------------

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

    // --- DateTime.Kind and the "O" round-trip format ------------------------

    [Fact]
    public void CanonicalBytes_UtcExpiry_EndsWithZ()
    {
        var license = NewLicenseWithExpiry();
        license.SubscriptionExpiryUtc = new DateTime(2027, 6, 1, 12, 30, 45, DateTimeKind.Utc);

        var actual = Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(license));

        Assert.EndsWith("Z", actual);
    }

    [Fact]
    public void CanonicalBytes_ExpiryFormat_DependsOnDateTimeKind()
    {
        // Same wall-clock ticks, three Kinds -> "O" emits three different suffixes.
        var ticks = new DateTime(2027, 6, 1, 12, 30, 45);

        var utc = NewLicenseWithExpiry();
        utc.SubscriptionExpiryUtc = DateTime.SpecifyKind(ticks, DateTimeKind.Utc);
        var unspecified = NewLicenseWithExpiry();
        unspecified.SubscriptionExpiryUtc = DateTime.SpecifyKind(ticks, DateTimeKind.Unspecified);
        var local = NewLicenseWithExpiry();
        local.SubscriptionExpiryUtc = DateTime.SpecifyKind(ticks, DateTimeKind.Local);

        // Compare only the expiry field (everything after the final '|').
        string ExpiryField(License l) =>
            Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(l)).Split('|')[^1];

        var utcExpiry = ExpiryField(utc);
        var unspecifiedExpiry = ExpiryField(unspecified);
        var localExpiry = ExpiryField(local);

        // Utc -> trailing 'Z'; Unspecified -> no zone marker at all.
        Assert.EndsWith("Z", utcExpiry);
        Assert.False(unspecifiedExpiry.EndsWith("Z", StringComparison.Ordinal));
        Assert.NotEqual(utcExpiry, unspecifiedExpiry);

        // Timezone-independent assertions: each field equals the value re-rendered
        // with the same invariant "O" specifier for that Kind.
        Assert.Equal(
            DateTime.SpecifyKind(ticks, DateTimeKind.Unspecified).ToString("O", CultureInfo.InvariantCulture),
            unspecifiedExpiry);
        Assert.Equal(
            DateTime.SpecifyKind(ticks, DateTimeKind.Local).ToString("O", CultureInfo.InvariantCulture),
            localExpiry);
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

        // Round-trip holds for any Kind as long as the in-memory License is unchanged.
        // NOTE (known gap): if the License is persisted and reloaded with a different
        // Kind (e.g. Npgsql returns Unspecified), the "O" text changes and Verify
        // would fail. CanonicalBytes does not normalise to UTC. See review notes.
        Assert.True(signer.Verify(license, signature, MatchingPublicKey()));
    }

    // --- ModelSnapshot values ---------------------------------------------

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

    // --- MaxActivations boundary values ------------------------------------

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

    // --- ProductId / LicenseKey edge values -------------------------------

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
    public void SignThenVerify_WithEmptyLicenseKey_RoundTripsAndCanonicalStartsWithPipe()
    {
        var signer = new LicenseSigner(_rsa);
        var license = NewLicenseWithoutExpiry();
        license.LicenseKey = string.Empty;

        var signature = signer.Sign(license);

        Assert.True(signer.Verify(license, signature, MatchingPublicKey()));
        Assert.StartsWith("|", Encoding.UTF8.GetString(LicenseSigner.CanonicalBytes(license)));
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

        Assert.StartsWith("AAAA|1111-2222|9|", canonical);

        var signer = new LicenseSigner(_rsa);
        var signature = signer.Sign(license);
        Assert.True(signer.Verify(license, signature, MatchingPublicKey()));
    }

    // --- expiry presence toggling ---------------------------------------

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

    // --- key material variants ------------------------------------------

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
