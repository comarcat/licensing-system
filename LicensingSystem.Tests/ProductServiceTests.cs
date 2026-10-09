using LicensingAdmin.Products;
using LicensingCore.Entities;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// Unit tests for <see cref="ProductService"/> over a fake <see cref="IProductStore"/>.
/// No database.
/// </summary>
public class ProductServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeStore : IProductStore
    {
        public readonly List<SoftwareProduct> Rows = new();
        public readonly Dictionary<Guid, int> LicenseCounts = new();
        public (SoftwareProduct product, AuditLogEntry audit)? Added;
        public (SoftwareProduct product, AuditLogEntry audit)? Updated;
        public AuditLogEntry? DeletedAudit;

        public Task<IReadOnlyList<SoftwareProduct>> ListAsync(bool includeArchived = false, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SoftwareProduct>>(
                includeArchived ? Rows : Rows.Where(p => !p.IsArchived).ToList());

        public Task<SoftwareProduct?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Rows.FirstOrDefault(p => p.Id == id));

        public Task AddAsync(SoftwareProduct product, AuditLogEntry audit, CancellationToken ct = default)
        {
            Added = (product, audit);
            Rows.Add(product);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(SoftwareProduct product, AuditLogEntry audit, CancellationToken ct = default)
        {
            var existing = Rows.First(p => p.Id == product.Id);
            existing.Name = product.Name;
            existing.Vendor = product.Vendor;
            existing.CurrentVersion = product.CurrentVersion;
            existing.DefaultLicenseModel = product.DefaultLicenseModel;
            existing.DefaultMaxActivations = product.DefaultMaxActivations;
            Updated = (product, audit);
            return Task.CompletedTask;
        }

        public Task<int> CountLicensesAsync(Guid productId, CancellationToken ct = default) =>
            Task.FromResult(LicenseCounts.GetValueOrDefault(productId, 0));

        public AuditLogEntry? ArchivedAudit { get; private set; }

        public Task ArchiveAsync(Guid productId, AuditLogEntry audit, CancellationToken ct = default)
        {
            var row = Rows.First(p => p.Id == productId);
            row.IsArchived = true;
            row.UpdatedAtUtc = DateTime.UtcNow;
            ArchivedAudit = audit;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid productId, AuditLogEntry audit, CancellationToken ct = default)
        {
            var count = LicenseCounts.GetValueOrDefault(productId, 0);
            if (count > 0)
            {
                throw new ProductInUseException(productId, count);
            }
            Rows.RemoveAll(p => p.Id == productId);
            DeletedAudit = audit;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ProductVersion>> ListVersionsAsync(Guid productId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProductVersion>>(new List<ProductVersion>());

        public Task AddVersionAsync(ProductVersion version, AuditLogEntry audit, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task UpdateVersionAsync(ProductVersion version, AuditLogEntry audit, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task DeleteVersionAsync(Guid versionId, AuditLogEntry audit, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    [Fact]
    public async Task CreateAsync_trims_fields_and_writes_a_created_audit_row()
    {
        var store = new FakeStore();
        var svc = new ProductService(store);

        var product = await svc.CreateAsync(
            "  InvoicePro  ", "  Acme  ", "  1.0  ", LicenseModel.Machine, 5, "boss@vendor.test", Ct);

        Assert.Equal("InvoicePro", product.Name);
        Assert.Equal("Acme", product.Vendor);
        Assert.Equal("1.0", product.CurrentVersion);
        Assert.NotNull(store.Added);
        Assert.Equal("Created", store.Added!.Value.audit.Action);
        Assert.Equal("SoftwareProduct", store.Added.Value.audit.EntityType);
        Assert.Equal("boss@vendor.test", store.Added.Value.audit.Actor);
    }

    [Fact]
    public async Task CreateAsync_rejects_blank_name_or_vendor()
    {
        var store = new FakeStore();
        var svc = new ProductService(store);

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CreateAsync("", "Acme", null, LicenseModel.Machine, 5, "boss@vendor.test", Ct));
        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CreateAsync("InvoicePro", " ", null, LicenseModel.Machine, 5, "boss@vendor.test", Ct));
        Assert.Empty(store.Rows);
    }

    [Fact]
    public async Task CreateAsync_rejects_max_activations_below_one()
    {
        var store = new FakeStore();
        var svc = new ProductService(store);

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CreateAsync("InvoicePro", "Acme", null, LicenseModel.Machine, 0, "boss@vendor.test", Ct));
    }

    [Fact]
    public async Task UpdateAsync_overwrites_the_existing_row_and_writes_an_updated_audit_row()
    {
        var store = new FakeStore();
        var svc = new ProductService(store);
        var product = await svc.CreateAsync("Old", "Acme", null, LicenseModel.Machine, 5, "boss@vendor.test", Ct);

        await svc.UpdateAsync(product.Id, "New", "Acme Corp", "2.0", LicenseModel.Subscription, 10, "boss@vendor.test", Ct);

        var row = store.Rows.Single();
        Assert.Equal("New", row.Name);
        Assert.Equal("Acme Corp", row.Vendor);
        Assert.Equal("2.0", row.CurrentVersion);
        Assert.Equal(LicenseModel.Subscription, row.DefaultLicenseModel);
        Assert.Equal(10, row.DefaultMaxActivations);
        Assert.Equal("Updated", store.Updated!.Value.audit.Action);
    }

    [Fact]
    public async Task ArchiveAsync_marks_product_archived_and_writes_audit_row()
    {
        var store = new FakeStore();
        var svc = new ProductService(store);
        var product = await svc.CreateAsync("Old", "Acme", null, LicenseModel.Machine, 5, "boss@vendor.test", Ct);

        await svc.ArchiveAsync(product.Id, "boss@vendor.test", Ct);

        Assert.True(product.IsArchived);
        Assert.NotNull(store.ArchivedAudit);
        Assert.Equal("Archived", store.ArchivedAudit!.Action);
        Assert.Equal("boss@vendor.test", store.ArchivedAudit.Actor);
    }

    [Fact]
    public async Task ListAsync_excludes_archived_products_by_default()
    {
        var store = new FakeStore();
        var svc = new ProductService(store);
        var visible = await svc.CreateAsync("Visible", "Acme", null, LicenseModel.Machine, 5, "boss@vendor.test", Ct);
        var archived = await svc.CreateAsync("Archived", "Acme", null, LicenseModel.Machine, 5, "boss@vendor.test", Ct);
        await svc.ArchiveAsync(archived.Id, "boss@vendor.test", Ct);

        var rows = await svc.ListAsync(Ct);
        var allRows = await svc.ListIncludingArchivedAsync(Ct);

        Assert.Single(rows);
        Assert.Equal(visible.Id, rows[0].Id);
        Assert.Equal(2, allRows.Count);
    }

    [Fact]
    public async Task ArchiveAsync_rejects_a_blank_actor()
    {
        var store = new FakeStore();
        var svc = new ProductService(store);
        var product = await svc.CreateAsync("Old", "Acme", null, LicenseModel.Machine, 5, "boss@vendor.test", Ct);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.ArchiveAsync(product.Id, " ", Ct));
        Assert.False(product.IsArchived);
    }

    [Fact]
    public async Task DeleteAsync_removes_a_product_with_no_licenses()
    {
        var store = new FakeStore();
        var svc = new ProductService(store);
        var product = await svc.CreateAsync("Old", "Acme", null, LicenseModel.Machine, 5, "boss@vendor.test", Ct);

        await svc.DeleteAsync(product.Id, "boss@vendor.test", Ct);

        Assert.Empty(store.Rows);
        Assert.NotNull(store.DeletedAudit);
        Assert.Equal("Deleted", store.DeletedAudit!.Action);
    }

    [Fact]
    public async Task DeleteAsync_refuses_a_product_still_referenced_by_licenses()
    {
        var store = new FakeStore();
        var svc = new ProductService(store);
        var product = await svc.CreateAsync("Old", "Acme", null, LicenseModel.Machine, 5, "boss@vendor.test", Ct);
        store.LicenseCounts[product.Id] = 3;

        var ex = await Assert.ThrowsAsync<ProductInUseException>(
            () => svc.DeleteAsync(product.Id, "boss@vendor.test", Ct));

        Assert.Equal(3, ex.LicenseCount);
        Assert.Single(store.Rows);
    }

    [Fact]
    public async Task DeleteAsync_rejects_a_blank_actor()
    {
        var store = new FakeStore();
        var svc = new ProductService(store);
        var product = await svc.CreateAsync("Old", "Acme", null, LicenseModel.Machine, 5, "boss@vendor.test", Ct);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.DeleteAsync(product.Id, " ", Ct));
        Assert.Single(store.Rows);
    }
}
