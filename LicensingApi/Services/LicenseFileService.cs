using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace LicensingApi.Services;

public interface ILicenseFileService
{
    /// <summary>
    /// Builds the license file the DLL persists locally: an XML document with the
    /// license/activation/policy state, signed (RSA-SHA256), returned as a base64
    /// string ready to embed in the API response or write to disk for offline exchange.
    /// </summary>
    string BuildSignedFile(LicenseFilePayload payload);
}

public class LicenseFilePayload
{
    public required string LicenseKey { get; set; }
    public required Guid ActivationId { get; set; }
    public required Guid InstallGuid { get; set; }
    public required string Status { get; set; } // approved | pending_review | locked
    public required string CpuId { get; set; }
    public required string MotherboardSerial { get; set; }
    public required string TpmId { get; set; }
    public required string MacAddressPrimary { get; set; }
    public int CheckIntervalHours { get; set; }
    public int GraceDays { get; set; }
    public int SubscriptionGraceDays { get; set; }
    public DateTime? SubscriptionExpiryUtc { get; set; }
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Signed-only license file (2026-09-13 — dropped the earlier AES-256-GCM encryption
/// layer). Nothing in this payload is actually confidential from the customer running
/// the software: the license key is theirs, the hardware IDs are their own machine's,
/// the dates aren't sensitive. The property that matters is integrity — a client must
/// be able to tell the file wasn't tampered with (status flipped to "approved", expiry
/// pushed out) — and RSA-SHA256 signing gives that completely, verified with the
/// PUBLIC key, which needs no distribution/secrecy at all. The previous scheme also
/// required a symmetric AES key on every client, which is a real secret with no clean
/// way to hand out to an external integrator without a manual, out-of-band step for
/// every single one — this removes that dependency entirely. If a real confidentiality
/// requirement ever emerges, the right fix is per-client asymmetric encryption (the
/// client generates its own keypair and sends the public half at /api/activate), not
/// reintroducing a shared symmetric secret.
/// </summary>
public class LicenseFileService : ILicenseFileService
{
    private readonly RSA _signingKey; // private key: sign here; the client ships with the public key

    public LicenseFileService(RSA signingKey)
    {
        _signingKey = signingKey;
    }

    public string BuildSignedFile(LicenseFilePayload p)
    {
        // A plain new XAttribute("xmlns", ns) alongside unqualified element names is not
        // equivalent to actually putting those elements in the namespace — .ToString()
        // throws XmlException ("prefix '' cannot be redefined...") because the writer
        // sees an element genuinely in the empty namespace carrying what looks like a
        // default-namespace declaration for its (unqualified, still-empty-namespace)
        // children. Every element must be explicitly qualified with XNamespace for the
        // declaration and the element tree to agree.
        XNamespace ns = "urn:licensing:v1";
        var doc = new XElement(ns + "LicenseActivation",
            new XElement(ns + "LicenseKey", p.LicenseKey),
            new XElement(ns + "ActivationId", p.ActivationId),
            new XElement(ns + "InstallGuid", p.InstallGuid),
            new XElement(ns + "Status", p.Status),
            new XElement(ns + "Hardware",
                new XElement(ns + "CpuId", p.CpuId),
                new XElement(ns + "MotherboardSerial", p.MotherboardSerial),
                new XElement(ns + "TpmId", p.TpmId),
                new XElement(ns + "MacAddressPrimary", p.MacAddressPrimary)),
            new XElement(ns + "Policy",
                new XElement(ns + "CheckIntervalHours", p.CheckIntervalHours),
                new XElement(ns + "GraceDays", p.GraceDays),
                new XElement(ns + "SubscriptionGraceDays", p.SubscriptionGraceDays)),
            new XElement(ns + "SubscriptionExpiryUtc", p.SubscriptionExpiryUtc?.ToString("O") ?? ""),
            new XElement(ns + "IssuedAtUtc", p.IssuedAtUtc.ToString("O"))
        );

        var canonicalBytes = Encoding.UTF8.GetBytes(doc.ToString(SaveOptions.DisableFormatting));

        var signature = _signingKey.SignData(canonicalBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        doc.Add(new XElement(ns + "Signature", Convert.ToBase64String(signature)));

        var signedBytes = Encoding.UTF8.GetBytes(doc.ToString(SaveOptions.DisableFormatting));
        return Convert.ToBase64String(signedBytes);
    }
}
