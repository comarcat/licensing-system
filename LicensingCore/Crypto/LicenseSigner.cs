using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LicensingCore.Entities;

namespace LicensingCore.Crypto;

/// <summary>
/// RSA-SHA256 (PKCS#1 v1.5) signer for <see cref="License"/> records. Mirrors the
/// signature idiom already used for per-activation license files
/// (<c>SignData(bytes, SHA256, Pkcs1)</c>).
/// </summary>
public sealed class LicenseSigner : ILicenseSigner
{
    private readonly RSA _signingKey;

    /// <param name="signingKey">
    /// RSA key holding the private material. The reference is stored, not copied; the
    /// caller owns its lifetime.
    /// </param>
    public LicenseSigner(RSA signingKey) => _signingKey = signingKey;

    /// <inheritdoc />
    public byte[] Sign(License license) =>
        _signingKey.SignData(
            CanonicalBytes(license), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    /// <inheritdoc />
    public bool Verify(License license, byte[] signature, RSA publicKey) =>
        publicKey.VerifyData(
            CanonicalBytes(license), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    /// <summary>
    /// Deterministic, locale-independent UTF-8 (no BOM) encoding of the security-relevant
    /// fields:
    /// <c>{LicenseKey}|{ProductId:D}|{(int)ModelSnapshot}|{MaxActivations}|{expiry}</c>
    /// where <c>expiry</c> is the empty string when <see cref="License.SubscriptionExpiryUtc"/>
    /// is <c>null</c>, otherwise its round-trip ("O") representation.
    /// </summary>
    public static byte[] CanonicalBytes(License license)
    {
        var expiry = license.SubscriptionExpiryUtc is { } expiryUtc
            ? expiryUtc.ToString("O", CultureInfo.InvariantCulture)
            : string.Empty;

        var canonical = FormattableString.Invariant(
            $"{license.LicenseKey}|{license.ProductId:D}|{(int)license.ModelSnapshot}|{license.MaxActivations}|{expiry}");

        return Encoding.UTF8.GetBytes(canonical);
    }
}
