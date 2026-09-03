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
    /// <summary>
    /// Domain-separation prefix (M-2): the same RSA key is also used by
    /// <c>LicenseFileService</c>; this literal makes a <see cref="LicenseSigner"/>
    /// signature impossible to confuse with one produced over a license file payload.
    /// </summary>
    private const string DomainPrefix = "licsig-v1|";

    private readonly RSA _signingKey;

    /// <param name="signingKey">
    /// RSA key holding the private material. The reference is stored, not copied; the
    /// caller owns its lifetime.
    /// </param>
    public LicenseSigner(RSA signingKey) =>
        _signingKey = signingKey ?? throw new ArgumentNullException(nameof(signingKey));

    /// <inheritdoc />
    public byte[] Sign(License license)
    {
        ArgumentNullException.ThrowIfNull(license);
        return _signingKey.SignData(
            CanonicalBytes(license), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    /// <inheritdoc />
    public bool Verify(License license, byte[] signature, RSA publicKey)
    {
        ArgumentNullException.ThrowIfNull(license);
        ArgumentNullException.ThrowIfNull(publicKey);
        if (signature is null or { Length: 0 }) return false;
        try
        {
            return publicKey.VerifyData(CanonicalBytes(license), signature,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// Deterministic, locale-independent UTF-8 (no BOM) encoding of the security-relevant
    /// fields:
    /// <c>licsig-v1|{LicenseKey}|{ProductId:D}|{(int)ModelSnapshot}|{MaxActivations}|{expiry}</c>
    /// where <c>expiry</c> is the empty string when <see cref="License.SubscriptionExpiryUtc"/>
    /// is <c>null</c>. Otherwise the value is normalized to UTC (<see cref="DateTimeKind.Local"/>
    /// via <see cref="DateTime.ToUniversalTime"/>; <see cref="DateTimeKind.Unspecified"/> treated
    /// as already UTC), truncated to whole seconds, and formatted
    /// <c>yyyy-MM-ddTHH:mm:ss'Z'</c> with <see cref="CultureInfo.InvariantCulture"/>. This keeps
    /// the signature stable across machines and across a database round-trip that changes
    /// <see cref="DateTime.Kind"/> or drops sub-second precision.
    /// </summary>
    public static byte[] CanonicalBytes(License license)
    {
        ArgumentNullException.ThrowIfNull(license);

        var expiry = license.SubscriptionExpiryUtc is { } expiryValue
            ? FormatExpiry(expiryValue)
            : string.Empty;

        var canonical = FormattableString.Invariant(
            $"{DomainPrefix}{license.LicenseKey}|{license.ProductId:D}|{(int)license.ModelSnapshot}|{license.MaxActivations}|{expiry}");

        return Encoding.UTF8.GetBytes(canonical);
    }

    private static string FormatExpiry(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value,
        };

        var truncated = new DateTime(
            utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);

        return truncated.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }
}
