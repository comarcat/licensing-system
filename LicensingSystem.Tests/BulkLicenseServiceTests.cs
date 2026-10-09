using LicensingAdmin.Licensing;
using LicensingAdmin.Notifications;
using LicensingAdmin.Products;
using LicensingAdmin.Services;
using LicensingCore.Crypto;
using LicensingCore.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LicensingSystem.Tests;

public class BulkLicenseServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly NoopProductStore FakeProductStore = new();
    private static readonly IEmailSender FakeEmailSender = new NoopEmailSender();
    private static readonly INotificationConfigStore FakeConfigStore = new NoopConfigStore();
    private static readonly IDataProtectionProvider FakeDataProtection = new NoopDataProtectionProvider();
    private static readonly ILogger<LicenseIssuanceService> Logger = new NoopLogger<LicenseIssuanceService>();

    [Fact]
    public async Task GenerateAsync_uses_specified_status()
    {
        var store = new FakeLicenseStore();
        var svc = new BulkLicenseService(new LicenseIssuanceService(new FakeSigner(), store, FakeProductStore, FakeEmailSender, FakeConfigStore, FakeDataProtection, Logger), FakeProductStore);
        var progress = new FakeProgress();
        var versionId = Guid.NewGuid();
        FakeProductStore.CurrentVersionId = versionId;

        var (_, licenses) = await svc.GenerateAsync(
            Guid.NewGuid(), versionId, 2, LicenseStatus.Test, "admin@vendor.test", progress, Ct);

        Assert.Equal(2, licenses.Count);
        Assert.All(licenses, l => Assert.Equal(LicenseStatus.Test, l.Status));
    }

    [Fact]
    public async Task GenerateAsync_defaults_to_active_status()
    {
        var store = new FakeLicenseStore();
        var svc = new BulkLicenseService(new LicenseIssuanceService(new FakeSigner(), store, FakeProductStore, FakeEmailSender, FakeConfigStore, FakeDataProtection, Logger), FakeProductStore);
        var progress = new FakeProgress();
        var versionId = Guid.NewGuid();
        FakeProductStore.CurrentVersionId = versionId;

        var (_, licenses) = await svc.GenerateAsync(
            Guid.NewGuid(), versionId, 1, LicenseStatus.Active, "admin@vendor.test", progress, Ct);

        var license = Assert.Single(licenses);
        Assert.Equal(LicenseStatus.Active, license.Status);
    }

    [Fact]
    public async Task GenerateAsync_reports_progress()
    {
        var store = new FakeLicenseStore();
        var svc = new BulkLicenseService(new LicenseIssuanceService(new FakeSigner(), store, FakeProductStore, FakeEmailSender, FakeConfigStore, FakeDataProtection, Logger), FakeProductStore);
        var progress = new FakeProgress();
        var versionId = Guid.NewGuid();
        FakeProductStore.CurrentVersionId = versionId;

        await svc.GenerateAsync(Guid.NewGuid(), versionId, 3, LicenseStatus.Active, "admin@vendor.test", progress, Ct);

        Assert.Equal(100.0, progress.LastValue);
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

        public Task AddAsync(SoftwareProduct product, AuditLogEntry auditProduct, IReadOnlyList<ProductVersion> versions, IReadOnlyList<AuditLogEntry> auditVersions, CancellationToken ct = default)
        {
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

    private sealed class NoopProductStore : IProductStore
    {
        public Task<IReadOnlyList<SoftwareProduct>> ListAsync(bool includeArchived = false, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SoftwareProduct>>(Array.Empty<SoftwareProduct>());

        public Task<SoftwareProduct?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<SoftwareProduct?>(new SoftwareProduct { Id = id, Name = "InvoicePro", Vendor = "Acme" });

        public Task AddAsync(SoftwareProduct product, AuditLogEntry auditProduct, IReadOnlyList<ProductVersion> versions, IReadOnlyList<AuditLogEntry> auditVersions, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task AddAsync(SoftwareProduct product, AuditLogEntry auditProduct, ProductVersion version, AuditLogEntry auditVersion, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task UpdateAsync(SoftwareProduct product, AuditLogEntry audit, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<int> CountLicensesAsync(Guid productId, CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task ArchiveAsync(Guid productId, AuditLogEntry audit, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(Guid productId, AuditLogEntry audit, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ProductVersion>> ListVersionsAsync(Guid productId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProductVersion>>(new[] { new ProductVersion { Id = CurrentVersionId, ProductId = productId, Name = "2.0" } });

        public Guid CurrentVersionId { get; set; }

        public Task AddVersionAsync(ProductVersion version, AuditLogEntry audit, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task UpdateVersionAsync(ProductVersion version, AuditLogEntry audit, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task DeleteVersionAsync(Guid versionId, AuditLogEntry audit, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopEmailSender : IEmailSender
    {
        public Task SendAsync(NotificationConfig config, string? plainPassword, string toAddress, string subject, string body, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopConfigStore : INotificationConfigStore
    {
        public Task<NotificationConfig?> GetAsync(CancellationToken ct = default) =>
            Task.FromResult<NotificationConfig?>(null);

        public Task SaveAsync(NotificationConfig config, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopDataProtectionProvider : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose) => new NoopDataProtector();
    }

    private sealed class NoopDataProtector : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;
        public byte[] Protect(byte[] plaintext) => plaintext;
        public byte[] Unprotect(byte[] protectedData) => protectedData;
    }

    private sealed class NoopLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
