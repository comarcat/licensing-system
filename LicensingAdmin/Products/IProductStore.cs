using LicensingCore.Data;
using LicensingCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace LicensingAdmin.Products;

/// <summary>
/// Persistence boundary for product catalog management (E4): list, create, update, and a
/// delete that only succeeds when no license references the product — mirrors the
/// database's own ON DELETE RESTRICT on licenses.product_id, just surfaced as a clear
/// domain exception instead of a raw Postgres 23503 foreign-key violation.
/// </summary>
public interface IProductStore
{
    Task<IReadOnlyList<SoftwareProduct>> ListAsync(CancellationToken ct = default);

    Task<SoftwareProduct?> FindByIdAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(SoftwareProduct product, AuditLogEntry audit, CancellationToken ct = default);

    Task UpdateAsync(SoftwareProduct product, AuditLogEntry audit, CancellationToken ct = default);

    /// <summary>How many licenses currently reference this product — used to explain a refused delete.</summary>
    Task<int> CountLicensesAsync(Guid productId, CancellationToken ct = default);

    /// <exception cref="ProductInUseException">The product still has at least one license.</exception>
    Task DeleteAsync(Guid productId, AuditLogEntry audit, CancellationToken ct = default);
}

/// <summary>EF Core-backed <see cref="IProductStore"/>. Not unit tested (thin EF adapter).</summary>
public sealed class EfProductStore(IDbContextFactory<AppDbContext> factory) : IProductStore
{
    public async Task<IReadOnlyList<SoftwareProduct>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.SoftwareProducts
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
    }

    public async Task<SoftwareProduct?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.SoftwareProducts.FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task AddAsync(SoftwareProduct product, AuditLogEntry audit, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.SoftwareProducts.Add(product);
        db.AuditLogEntries.Add(audit);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(SoftwareProduct product, AuditLogEntry audit, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.SoftwareProducts.FirstOrDefaultAsync(p => p.Id == product.Id, ct)
            ?? throw new InvalidOperationException($"No product with id '{product.Id}'.");
        existing.Name = product.Name;
        existing.Vendor = product.Vendor;
        existing.CurrentVersion = product.CurrentVersion;
        existing.DefaultLicenseModel = product.DefaultLicenseModel;
        existing.DefaultMaxActivations = product.DefaultMaxActivations;
        existing.UpdatedAtUtc = DateTime.UtcNow;
        db.AuditLogEntries.Add(audit);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> CountLicensesAsync(Guid productId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Licenses.CountAsync(l => l.ProductId == productId, ct);
    }

    public async Task DeleteAsync(Guid productId, AuditLogEntry audit, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var product = await db.SoftwareProducts.FirstOrDefaultAsync(p => p.Id == productId, ct)
            ?? throw new InvalidOperationException($"No product with id '{productId}'.");

        var licenseCount = await db.Licenses.CountAsync(l => l.ProductId == productId, ct);
        if (licenseCount > 0)
        {
            throw new ProductInUseException(productId, licenseCount);
        }

        db.SoftwareProducts.Remove(product);
        db.AuditLogEntries.Add(audit);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Thrown by <see cref="IProductStore.DeleteAsync"/> when the product still has at least
/// one license referencing it (the database's own ON DELETE RESTRICT would reject the
/// delete anyway; this catches it earlier with a message the UI can show directly).
/// </summary>
public sealed class ProductInUseException(Guid productId, int licenseCount)
    : InvalidOperationException(
        $"Cannot delete this product: {licenseCount} license(s) still reference it.")
{
    public Guid ProductId { get; } = productId;
    public int LicenseCount { get; } = licenseCount;
}
