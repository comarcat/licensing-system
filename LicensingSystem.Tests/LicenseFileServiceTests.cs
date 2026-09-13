using System.Security.Cryptography;
using System.Xml.Linq;
using LicensingApi.Services;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// Full round-trip coverage for <see cref="LicenseFileService.BuildSignedFile"/>:
/// build -&gt; verify (RSA-SHA256/PKCS1) -&gt; parse, exactly what a real client tool does
/// per docs/activation-dll-integration-reference.md. This did not exist before
/// 2026-09-13 — the method had zero test coverage, and its very first real invocation
/// (a live POST /api/activate against production) threw
/// <c>System.Xml.XmlException: The prefix '' cannot be redefined from '' to
/// 'urn:licensing:v1' within the same start element tag.</c> A plain
/// <c>new XAttribute("xmlns", ns)</c> alongside unqualified element names does not
/// actually put those elements in the namespace; every element must be built with
/// <c>XNamespace</c> for <see cref="XElement.ToString(SaveOptions)"/> to succeed at all.
///
/// The AES-256-GCM encryption layer this method originally added on top of the
/// signature was dropped the same day (2026-09-13): none of the payload is actually
/// confidential from the customer running the software, and requiring every external
/// integrator to receive a shared symmetric key out of band was a real deployment
/// blocker for no real security benefit — the RSA signature alone already gives
/// tamper-evidence, verified with the public key, which needs no secrecy at all.
/// </summary>
public class LicenseFileServiceTests
{
    private static readonly XNamespace Ns = "urn:licensing:v1";

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

    /// <summary>Verify + parse, exactly as documented for the client tool.</summary>
    private static XElement VerifyAndParse(string base64File, RSA publicKey)
    {
        var signedBytes = Convert.FromBase64String(base64File);
        var xml = XElement.Parse(System.Text.Encoding.UTF8.GetString(signedBytes));
        var signatureElement = xml.Element(Ns + "Signature")
            ?? throw new InvalidOperationException("No <Signature> element in the XML.");
        var signature = Convert.FromBase64String(signatureElement.Value);
        signatureElement.Remove();

        var canonicalBytes = System.Text.Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting));
        var verified = publicKey.VerifyData(
            canonicalBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Assert.True(verified, "RSA signature did not verify against the canonicalized (Signature-less) XML.");

        return xml;
    }

    [Fact]
    public void BuildSignedFile_does_not_throw()
    {
        var rsa = RSA.Create(2048);
        var service = new LicenseFileService(rsa);

        var exception = Record.Exception(() => service.BuildSignedFile(SamplePayload()));

        Assert.Null(exception);
    }

    [Fact]
    public void BuildSignedFile_round_trips_verify_and_every_field()
    {
        var rsa = RSA.Create(2048);
        var service = new LicenseFileService(rsa);
        var payload = SamplePayload();

        var base64 = service.BuildSignedFile(payload);
        var xml = VerifyAndParse(base64, rsa);

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
    public void BuildSignedFile_with_no_subscription_expiry_writes_an_empty_element()
    {
        var rsa = RSA.Create(2048);
        var service = new LicenseFileService(rsa);
        var payload = SamplePayload();
        payload.SubscriptionExpiryUtc = null;

        var base64 = service.BuildSignedFile(payload);
        var xml = VerifyAndParse(base64, rsa);

        Assert.Equal("", xml.Element(Ns + "SubscriptionExpiryUtc")?.Value);
    }

    [Fact]
    public void BuildSignedFile_signature_does_not_verify_against_a_different_public_key()
    {
        var rsa = RSA.Create(2048);
        var service = new LicenseFileService(rsa);
        var base64 = service.BuildSignedFile(SamplePayload());

        using var wrongKey = RSA.Create(2048);
        var signedBytes = Convert.FromBase64String(base64);
        var xml = XElement.Parse(System.Text.Encoding.UTF8.GetString(signedBytes));
        var signature = Convert.FromBase64String(xml.Element(Ns + "Signature")!.Value);
        xml.Element(Ns + "Signature")!.Remove();
        var canonicalBytes = System.Text.Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting));

        var verified = wrongKey.VerifyData(
            canonicalBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        Assert.False(verified);
    }
}
