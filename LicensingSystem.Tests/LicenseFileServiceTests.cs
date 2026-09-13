using System.Security.Cryptography;
using System.Xml.Linq;
using LicensingApi.Services;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// Full round-trip coverage for <see cref="LicenseFileService.BuildSignedEncryptedFile"/>:
/// build -&gt; decrypt (AES-256-GCM) -&gt; verify (RSA-SHA256/PKCS1) -&gt; parse, exactly what a
/// real client tool does per docs/activation-dll-integration-reference.md. This did not
/// exist before 2026-09-13 — the method had zero test coverage, and its very first real
/// invocation (a live POST /api/activate against production) threw
/// <c>System.Xml.XmlException: The prefix '' cannot be redefined from '' to
/// 'urn:licensing:v1' within the same start element tag.</c> A plain
/// <c>new XAttribute("xmlns", ns)</c> alongside unqualified element names does not
/// actually put those elements in the namespace; every element must be built with
/// <c>XNamespace</c> for <see cref="XElement.ToString(SaveOptions)"/> to succeed at all.
/// </summary>
public class LicenseFileServiceTests
{
    private static readonly XNamespace Ns = "urn:licensing:v1";

    private static (RSA rsa, byte[] aesKey) NewKeys()
    {
        var rsa = RSA.Create(2048);
        var aesKey = RandomNumberGenerator.GetBytes(32);
        return (rsa, aesKey);
    }

    private static LicenseFilePayload SamplePayload() => new()
    {
        LicenseKey = "BRCX-IH9BC-X9ZV-KQXC-2NLY-CO03-8P",
        ActivationId = Guid.NewGuid(),
        InstallGuid = Guid.NewGuid(),
        Status = "approved",
        CpuId = "TEST-CPU-001",
        MotherboardSerial = "TEST-MB-001",
        TpmId = "TEST-TPM-001",
        MacAddressPrimary = "00:00:00:TE:ST:01",
        CheckIntervalHours = 6,
        GraceDays = 15,
        SubscriptionGraceDays = 30,
        SubscriptionExpiryUtc = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        IssuedAtUtc = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc),
    };

    /// <summary>Decrypt + verify + parse, exactly as documented for the client tool.</summary>
    private static XElement DecryptVerifyAndParse(string base64Envelope, RSA publicKey, byte[] aesKey)
    {
        var envelope = Convert.FromBase64String(base64Envelope);
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
            ?? throw new InvalidOperationException("No <Signature> element in the decrypted XML.");
        var signature = Convert.FromBase64String(signatureElement.Value);
        signatureElement.Remove();

        var canonicalBytes = System.Text.Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting));
        var verified = publicKey.VerifyData(
            canonicalBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Assert.True(verified, "RSA signature did not verify against the canonicalized (Signature-less) XML.");

        return xml;
    }

    [Fact]
    public void BuildSignedEncryptedFile_does_not_throw()
    {
        var (rsa, aesKey) = NewKeys();
        var service = new LicenseFileService(rsa, aesKey);

        var exception = Record.Exception(() => service.BuildSignedEncryptedFile(SamplePayload()));

        Assert.Null(exception);
    }

    [Fact]
    public void BuildSignedEncryptedFile_round_trips_decrypt_verify_and_every_field()
    {
        var (rsa, aesKey) = NewKeys();
        var service = new LicenseFileService(rsa, aesKey);
        var payload = SamplePayload();

        var base64 = service.BuildSignedEncryptedFile(payload);
        var xml = DecryptVerifyAndParse(base64, rsa, aesKey);

        Assert.Equal(payload.LicenseKey, xml.Element(Ns + "LicenseKey")?.Value);
        Assert.Equal(payload.ActivationId.ToString(), xml.Element(Ns + "ActivationId")?.Value);
        Assert.Equal(payload.InstallGuid.ToString(), xml.Element(Ns + "InstallGuid")?.Value);
        Assert.Equal(payload.Status, xml.Element(Ns + "Status")?.Value);

        var hardware = xml.Element(Ns + "Hardware")!;
        Assert.Equal(payload.CpuId, hardware.Element(Ns + "CpuId")?.Value);
        Assert.Equal(payload.MotherboardSerial, hardware.Element(Ns + "MotherboardSerial")?.Value);
        Assert.Equal(payload.TpmId, hardware.Element(Ns + "TpmId")?.Value);
        Assert.Equal(payload.MacAddressPrimary, hardware.Element(Ns + "MacAddressPrimary")?.Value);

        var policy = xml.Element(Ns + "Policy")!;
        Assert.Equal("6", policy.Element(Ns + "CheckIntervalHours")?.Value);
        Assert.Equal("15", policy.Element(Ns + "GraceDays")?.Value);
        Assert.Equal("30", policy.Element(Ns + "SubscriptionGraceDays")?.Value);

        Assert.Equal(
            payload.SubscriptionExpiryUtc!.Value.ToString("O"),
            xml.Element(Ns + "SubscriptionExpiryUtc")?.Value);
        Assert.Equal(payload.IssuedAtUtc.ToString("O"), xml.Element(Ns + "IssuedAtUtc")?.Value);
    }

    [Fact]
    public void BuildSignedEncryptedFile_with_no_subscription_expiry_writes_an_empty_element()
    {
        var (rsa, aesKey) = NewKeys();
        var service = new LicenseFileService(rsa, aesKey);
        var payload = SamplePayload();
        payload.SubscriptionExpiryUtc = null;

        var base64 = service.BuildSignedEncryptedFile(payload);
        var xml = DecryptVerifyAndParse(base64, rsa, aesKey);

        Assert.Equal("", xml.Element(Ns + "SubscriptionExpiryUtc")?.Value);
    }

    [Fact]
    public void BuildSignedEncryptedFile_signature_does_not_verify_against_a_different_public_key()
    {
        var (rsa, aesKey) = NewKeys();
        var service = new LicenseFileService(rsa, aesKey);
        var base64 = service.BuildSignedEncryptedFile(SamplePayload());

        using var wrongKey = RSA.Create(2048);
        var envelope = Convert.FromBase64String(base64);
        var nonce = envelope[..12];
        var tag = envelope[12..28];
        var cipherText = envelope[28..];
        var plainBytes = new byte[cipherText.Length];
        using (var aes = new AesGcm(aesKey, tag.Length))
        {
            aes.Decrypt(nonce, cipherText, tag, plainBytes);
        }
        var xml = XElement.Parse(System.Text.Encoding.UTF8.GetString(plainBytes));
        var signature = Convert.FromBase64String(xml.Element(Ns + "Signature")!.Value);
        xml.Element(Ns + "Signature")!.Remove();
        var canonicalBytes = System.Text.Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting));

        var verified = wrongKey.VerifyData(
            canonicalBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        Assert.False(verified);
    }
}
