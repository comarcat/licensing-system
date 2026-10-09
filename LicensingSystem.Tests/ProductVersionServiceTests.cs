using LicensingAdmin.Products;
using LicensingCore.Entities;
using Xunit;

namespace LicensingSystem.Tests;

public class ProductVersionServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeStore : IProductStore
    {
        public readonly List<SoftwareProduct> Products = new();
        public readonly List<ProductVersion> Versions = new();
        public (ProductVersion version, AuditLogEntry audit)? AddedVersion;
        public (ProductVersion version, AuditLogEntry audit)? UpdatedVersion;
        public AuditLogEntry? DeletedVersionAudit;

        public Task<IReadOnlyList<SoftwareProduct>> ListAsync(bool includeArchived = false, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SoftwareProduct>>(includeArchived ? Products : Products.Where(p => !p.IsArchived).ToList());

        public Task<SoftwareProduct?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Products.FirstOrDefault(p => p.Id == id));

        public Task AddAsync(SoftwareProduct product, AuditLogEntry auditProduct, IReadOnlyList<ProductVersion> versions, IReadOnlyList<AuditLogEntry> auditVersions, CancellationToken ct = default)
        {
            Products.Add(product);
            return Task.CompletedTask;
        }

        public Task AddAsync(SoftwareProduct product, AuditLogEntry auditProduct, ProductVersion version, AuditLogEntry auditVersion, CancellationToken ct = default)
        {
            Products.Add(product);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(SoftwareProduct product, AuditLogEntry audit, CancellationToken ct = default) => Task.CompletedTask;

        public Task<int> CountLicensesAsync(Guid productId, CancellationToken ct = default) => Task.FromResult(0);

        public Task ArchiveAsync(Guid productId, AuditLogEntry audit, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteAsync(Guid productId, AuditLogEntry audit, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<ProductVersion>> ListVersionsAsync(Guid productId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProductVersion>>(Versions.Where(v => v.ProductId == productId).ToList());

        public Task AddVersionAsync(ProductVersion version, AuditLogEntry audit, CancellationToken ct = default)
        {
            AddedVersion = (version, audit);
            Versions.Add(version);
            return Task.CompletedTask;
        }

        public Task UpdateVersionAsync(ProductVersion version, AuditLogEntry audit, CancellationToken ct = default)
        {
            UpdatedVersion = (version, audit);
            var existing = Versions.First(v => v.Id == version.Id);
            existing.Name = version.Name;
            return Task.CompletedTask;
        }

        public Task DeleteVersionAsync(Guid versionId, AuditLogEntry audit, CancellationToken ct = default)
        {
            DeletedVersionAudit = audit;
            Versions.RemoveAll(v => v.Id == versionId);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task CreateVersionAsync_persists_version_and_writes_audit()
    {
        var store = new FakeStore();
        var svc = new ProductService(store);
        var productId = Guid.NewGuid();

        var version = await svc.CreateVersionAsync(productId, "  Premium  ", "admin@vendor.test", Ct);

        Assert.Equal("Premium", version.Name);
        Assert.Equal(productId, version.ProductId);
        Assert.NotNull(store.AddedVersion);
        Assert.Equal("Created", store.AddedVersion!.Value.audit.Action);
        Assert.Equal("ProductVersion", store.AddedVersion.Value.audit.EntityType);
        Assert.Equal("admin@vendor.test", store.AddedVersion.Value.audit.Actor);
    }

    [Fact]
    public async Task CreateVersionAsync_rejects_blank_name_or_actor()
    {
        var store = new FakeStore();
        var svc = new ProductService(store);
        var productId = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateVersionAsync(productId, "", "admin@vendor.test", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateVersionAsync(productId, "Premium", " ", Ct));
    }

    [Fact]
    public async Task UpdateVersionAsync_updates_name_and_writes_audit()
    {
        var store = new FakeStore();
        var svc = new ProductService(store);
        var versionId = Guid.NewGuid();
        store.Versions.Add(new ProductVersion { Id = versionId, Name = "Old", ProductId = Guid.NewGuid() });

        await svc.UpdateVersionAsync(versionId, "  New  ", "admin@vendor.test", Ct);

        Assert.Equal("New", store.Versions[0].Name);
        Assert.NotNull(store.UpdatedVersion);
        Assert.Equal("Updated", store.UpdatedVersion!.Value.audit.Action);
        Assert.Equal("ProductVersion", store.UpdatedVersion.Value.audit.EntityType);
    }

    [Fact]
    public async Task DeleteVersionAsync_removes_version_and_writes_audit()
    {
        var store = new FakeStore();
        var svc = new ProductService(store);
        var versionId = Guid.NewGuid();
        store.Versions.Add(new ProductVersion { Id = versionId, Name = "To Delete", ProductId = Guid.NewGuid() });

        await svc.DeleteVersionAsync(versionId, "admin@vendor.test", Ct);

        Assert.Empty(store.Versions);
        Assert.NotNull(store.DeletedVersionAudit);
        Assert.Equal("Deleted", store.DeletedVersionAudit!.Action);
        Assert.Equal("ProductVersion", store.DeletedVersionAudit.EntityType);
    }
}
