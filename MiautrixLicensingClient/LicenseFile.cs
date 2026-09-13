using System.Security.Cryptography;
using System.Xml.Linq;

namespace Miautrix.Licensing.Client;

/// <summary>
/// Verifies and parses <c>ActivationResultData.LicenseFileBase64</c> exactly per
/// activation-dll-integration-reference.md §5: base64-decode, RSA-SHA256/PKCS1 verify
/// over the canonicalized (Signature-less, unindented) XML, then parse. As of
/// 2026-09-13 this is signed only — no encryption layer, no symmetric key needed.
/// </summary>
public static class LicenseFile
{
    private static readonly XNamespace Ns = "urn:licensing:v1";

    /// <param name="base64File"><c>ActivationResultData.LicenseFileBase64</c>.</param>
    /// <param name="publicKey">
    /// The RSA public key from activation-dll-integration-reference.md §5.5 (safe to
    /// embed — see <see cref="Keys.LicensingPublicKeyPem"/>).
    /// </param>
    /// <exception cref="InvalidDataException">Malformed file or missing &lt;Signature&gt;.</exception>
    public static LicenseFileContents VerifyAndParse(string base64File, RSA publicKey)
    {
        var signedBytes = Convert.FromBase64String(base64File);
        var xml = XElement.Parse(System.Text.Encoding.UTF8.GetString(signedBytes));
        var signatureElement = xml.Element(Ns + "Signature")
            ?? throw new InvalidDataException("License file XML has no <Signature> element.");
        var signature = Convert.FromBase64String(signatureElement.Value);
        signatureElement.Remove();

        var canonicalBytes = System.Text.Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting));
        var verified = publicKey.VerifyData(canonicalBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return new LicenseFileContents
        {
            SignatureVerified = verified,
            LicenseKey = xml.Element(Ns + "LicenseKey")?.Value ?? "",
            ActivationId = Guid.Parse(xml.Element(Ns + "ActivationId")?.Value ?? Guid.Empty.ToString()),
            InstallGuid = Guid.Parse(xml.Element(Ns + "InstallGuid")?.Value ?? Guid.Empty.ToString()),
            Status = xml.Element(Ns + "Status")?.Value ?? "",
            SubscriptionExpiryUtc = string.IsNullOrEmpty(xml.Element(Ns + "SubscriptionExpiryUtc")?.Value)
                ? null
                : DateTime.Parse(xml.Element(Ns + "SubscriptionExpiryUtc")!.Value).ToUniversalTime(),
            IssuedAtUtc = DateTime.Parse(xml.Element(Ns + "IssuedAtUtc")?.Value ?? DateTime.MinValue.ToString("O")).ToUniversalTime(),
        };
    }
}

public class LicenseFileContents
{
    /// <summary>
    /// Always check this before trusting anything else on this object. If false, treat
    /// the license as tampered — do not honor <see cref="Status"/> or
    /// <see cref="SubscriptionExpiryUtc"/>.
    /// </summary>
    public bool SignatureVerified { get; set; }
    public string LicenseKey { get; set; } = "";
    public Guid ActivationId { get; set; }
    public Guid InstallGuid { get; set; }
    public string Status { get; set; } = "";
    public DateTime? SubscriptionExpiryUtc { get; set; }
    public DateTime IssuedAtUtc { get; set; }
}
