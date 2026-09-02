using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace LicensingApi.Services;

public interface ILicenseFileService
{
    /// <summary>
    /// Builds the license file the DLL persists locally: an XML document with the
    /// license/activation/policy state, signed (RSA-SHA256) then AES-GCM encrypted,
    /// returned as a base64 string ready to embed in the API response or write to disk
    /// for offline exchange.
    /// </summary>
    string BuildSignedEncryptedFile(LicenseFilePayload payload);
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
/// NOTE: this is a pragmatic sign-then-encrypt envelope (RSA-SHA256 detached signature,
/// AES-256-GCM encryption), not full W3C XMLDSig/XMLEncrypt ceremony. It gives the same
/// security properties (authenticity + confidentiality) with far less code to maintain.
/// If you specifically need XMLDSig/XMLEncrypt interop with another system, swap this
/// implementation for System.Security.Cryptography.Xml — the payload shape (LicenseFilePayload)
/// and the DLL-side contract (decrypt -> verify signature -> parse) stay the same either way.
/// </summary>
public class LicenseFileService : ILicenseFileService
{
    private readonly RSA _signingKey;   // private key: sign here; DLL ships with the public key
    private readonly byte[] _aesKey;    // 32 bytes; see README for key management guidance

    public LicenseFileService(RSA signingKey, byte[] aesKey)
    {
        _signingKey = signingKey;
        _aesKey = aesKey;
    }

    public string BuildSignedEncryptedFile(LicenseFilePayload p)
    {
        var doc = new XElement("LicenseActivation",
            new XAttribute("xmlns", "urn:licensing:v1"),
            new XElement("LicenseKey", p.LicenseKey),
            new XElement("ActivationId", p.ActivationId),
            new XElement("InstallGuid", p.InstallGuid),
            new XElement("Status", p.Status),
            new XElement("Hardware",
                new XElement("CpuId", p.CpuId),
                new XElement("MotherboardSerial", p.MotherboardSerial),
                new XElement("TpmId", p.TpmId),
                new XElement("MacAddressPrimary", p.MacAddressPrimary)),
            new XElement("Policy",
                new XElement("CheckIntervalHours", p.CheckIntervalHours),
                new XElement("GraceDays", p.GraceDays),
                new XElement("SubscriptionGraceDays", p.SubscriptionGraceDays)),
            new XElement("SubscriptionExpiryUtc", p.SubscriptionExpiryUtc?.ToString("O") ?? ""),
            new XElement("IssuedAtUtc", p.IssuedAtUtc.ToString("O"))
        );

        var canonicalBytes = Encoding.UTF8.GetBytes(doc.ToString(SaveOptions.DisableFormatting));

        // 1. Sign (RSA-SHA256) over the canonical XML bytes.
        var signature = _signingKey.SignData(canonicalBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        doc.Add(new XElement("Signature", Convert.ToBase64String(signature)));

        var signedBytes = Encoding.UTF8.GetBytes(doc.ToString(SaveOptions.DisableFormatting));

        // 2. Encrypt the signed document (AES-256-GCM).
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipherText = new byte[signedBytes.Length];
        using (var aes = new AesGcm(_aesKey, tag.Length))
        {
            aes.Encrypt(nonce, signedBytes, cipherText, tag);
        }

        // Envelope: nonce (12) || tag (16) || ciphertext, base64-encoded.
        var envelope = new byte[nonce.Length + tag.Length + cipherText.Length];
        Buffer.BlockCopy(nonce, 0, envelope, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, envelope, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherText, 0, envelope, nonce.Length + tag.Length, cipherText.Length);

        return Convert.ToBase64String(envelope);
    }
}
