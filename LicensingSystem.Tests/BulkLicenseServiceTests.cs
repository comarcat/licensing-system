using LicensingAdmin.Licensing;
using LicensingAdmin.Services;
using LicensingCore.Crypto;
using LicensingCore.Entities;
using Xunit;

namespace LicensingSystem.Tests;

public class BulkLicenseServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GenerateAsync_uses_specified_status()
    {
        var store = new FakeLicenseStore();
        var svc = new BulkLicenseService(new LicenseIssuanceService(new FakeSigner(), store));
        var progress = new FakeProgress();

        var (_, licenses) = await svc.GenerateAsync(
            Guid.NewGuid(), Guid.NewGuid(), 2, LicenseStatus.Test, "admin@vendor.test", progress, Ct);

        Assert.Equal(2, licenses.Count);
        Assert.All(licenses, l => Assert.Equal(LicenseStatus.Test, l.Status));
    }

    [Fact]
    public async Task GenerateAsync_defaults_to_active_status()
    {
        var store = new FakeLicenseStore();
        var svc = new BulkLicenseService(new LicenseIssuanceService(new FakeSigner(), store));
        var progress = new FakeProgress();

        var (_, licenses) = await svc.GenerateAsync(
            Guid.NewGuid(), Guid.NewGuid(), 1, LicenseStatus.Active, "admin@vendor.test", progress, Ct);

        var license = Assert.Single(licenses);
        Assert.Equal(LicenseStatus.Active, license.Status);
    }

    [Fact]
    public async Task GenerateAsync_reports_progress()
    {
        var store = new FakeLicenseStore();
        var svc = new BulkLicenseService(new LicenseIssuanceService(new FakeSigner(), store));
        var reported = new List<double>();
        var progress = new Progress<double>(p => reported.Add(p));

        await svc.GenerateAsync(Guid.NewGuid(), Guid.NewGuid(), 3, LicenseStatus.Active, "admin@vendor.test", progress, Ct);

        Assert.NotEmpty(reported);
        Assert.Contains(100.0, reported);
    }

    private sealed class FakeLicenseStore : ILicenseStore
    {
        public readonly List<string> KeysChecked = new();
        public readonly List<AuditLogEntry> Audits = new();
        public License? AddedLicense;

        public Task<bool> LicenseKeyExistsAsync(string licenseKey, CancellationToken ct = default)
        {
            KeysChecked.Add(licenseKey);
            return Task.FromResult(false);
        }

        public Task AddAsync(SoftwareProduct? newProduct, ProductVersion? newVersion, License license, AuditLogEntry audit, CancellationToken ct = default)
        {
            AddedLicense = license;
            Audits.Add(audit);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSigner : ILicenseSigner
    {
        public byte[] Sign(License license) => new byte[] { 1, 2, 3 };
        public bool Verify(License license, byte[] signature, System.Security.Cryptography.RSA publicKey) => true;
    }

    private sealed class FakeProgress : IProgress<double>
    {
        public double LastValue { get; private set; }
        public void Report(double value) => LastValue = value;
    }
}
