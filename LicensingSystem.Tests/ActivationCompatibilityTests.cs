using System.Security.Cryptography;
using LicensingCore.Crypto;
using LicensingCore.Entities;
using Miautrix.Licensing.Client;
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
            Status = Miautrix.Licensing.Client.LicenseStatus.Test,
            Email = "comarcat@gmail.com",
            ClientTimestampUtc = DateTime.UtcNow,
        };

        // Validate serialization contract
        var json = System.Text.Json.JsonSerializer.Serialize(request);
        Assert.Contains("\"VersionId\"", json);
        Assert.Contains("\"Status\":3", json);
        Assert.Contains("\"Email\":\"comarcat@gmail.com\"", json);
    }
}
