using System.Security.Cryptography;
using System.Xml.Linq;

namespace ActivationHelloWorld;

/// <summary>
/// Decrypts and verifies <c>ActivationResultData.LicenseFileBase64</c> exactly per
/// docs/activation-dll-integration-reference.md §5: AES-256-GCM decrypt, then
/// RSA-SHA256/PKCS1 verify over the canonicalized (Signature-less, unindented) XML.
/// </summary>
public static class LicenseFile
{
    private static readonly XNamespace Ns = "urn:licensing:v1";

    public static LicenseFileContents DecryptAndVerify(string base64Envelope, byte[] aesKey, RSA publicKey)
    {
        var envelope = Convert.FromBase64String(base64Envelope);
        if (envelope.Length < 28)
            throw new InvalidDataException("License file envelope is too short to contain nonce+tag.");

        var nonce = envelope[..12];
        var tag = envelope[12..28];
        var cipherText = envelope[28..];

        var plainBytes = new byte[cipherText.Length];
        using (var aes = new AesGcm(aesKey, tag.Length))
        {
            aes.Decrypt(nonce, cipherText, tag, plainBytes);
        }

        var xml = XElement.Parse(System.Text.Encoding.UTF8.GetString(plainBytes));
        var signatureElement = xml.Element(Ns + "Signature")
            ?? throw new InvalidDataException("Decrypted XML has no <Signature> element.");
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
    public bool SignatureVerified { get; set; }
    public string LicenseKey { get; set; } = "";
    public Guid ActivationId { get; set; }
    public Guid InstallGuid { get; set; }
    public string Status { get; set; } = "";
    public DateTime? SubscriptionExpiryUtc { get; set; }
    public DateTime IssuedAtUtc { get; set; }
}
