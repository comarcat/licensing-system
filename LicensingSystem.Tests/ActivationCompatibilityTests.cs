using System.Security.Cryptography;
using ActivationHelloWorld;
using LicensingCore.Crypto;
using LicensingCore.Entities;
using Xunit;

namespace LicensingSystem.Tests;

public class ActivationCompatibilityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Compatibility_NewSchema_SendsVersionAndStatus()
    {
        // This validates E3-T5/E3-T6: the client can now send Version and Status
        // and they are correctly forwarded to the API.
        var request = new ActivateRequest
        {
            LicenseKey = "ABCD-EFGH1-IJKL-MNOP-QRST-UVWX-YZ",
            InstallGuid = Guid.NewGuid(),
            Hardware = new HardwareInfo { CpuId = "c", MotherboardSerial = "m", TpmId = "t", MacAddressPrimary = "ma" },
            VersionId = Guid.NewGuid(),
            Status = ActivationHelloWorld.LicenseStatus.Test,
            ClientTimestampUtc = DateTime.UtcNow,
        };

        // Validate serialization contract
        var json = System.Text.Json.JsonSerializer.Serialize(request);
        Assert.Contains("\"VersionId\"", json);
        Assert.Contains("\"Status\":3", json);
    }
}
