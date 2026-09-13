using System.Text.Json;
using LicensingCore.Entities;

namespace LicensingAdmin.Products;

/// <summary>
/// CRUD use cases behind the product catalog screen (E4 item 8). Every write records an
/// <see cref="AuditLogEntry"/>, matching the pattern established by
/// <see cref="LicensingAdmin.Auth.AdminUserService"/>.
/// </summary>
public sealed class ProductService(IProductStore store)
{
    public Task<IReadOnlyList<SoftwareProduct>> ListAsync(CancellationToken ct = default) => store.ListAsync(ct);

    public Task<int> CountLicensesAsync(Guid productId, CancellationToken ct = default) =>
        store.CountLicensesAsync(productId, ct);

    /// <exception cref="ArgumentException"><paramref name="name"/>, <paramref name="vendor"/> or <paramref name="actor"/> is null/blank.</exception>
    public async Task<SoftwareProduct> CreateAsync(
        string name, string vendor, string? currentVersion, LicenseModel defaultLicenseModel,
        int defaultMaxActivations, string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(vendor);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (defaultMaxActivations < 1)
        {
            throw new ArgumentException("Default max activations must be at least 1.", nameof(defaultMaxActivations));
        }

        var product = new SoftwareProduct
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Vendor = vendor.Trim(),
            CurrentVersion = string.IsNullOrWhiteSpace(currentVersion) ? null : currentVersion.Trim(),
            DefaultLicenseModel = defaultLicenseModel,
            DefaultMaxActivations = defaultMaxActivations,
        };

        var audit = Audit(actor, product.Id, "Created");
        audit.DetailsJson = JsonSerializer.Serialize(new { name = product.Name });
        await store.AddAsync(product, audit, ct);
        return product;
    }

    /// <exception cref="ArgumentException"><paramref name="name"/>, <paramref name="vendor"/> or <paramref name="actor"/> is null/blank.</exception>
    public async Task UpdateAsync(
        Guid id, string name, string vendor, string? currentVersion, LicenseModel defaultLicenseModel,
        int defaultMaxActivations, string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(vendor);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (defaultMaxActivations < 1)
        {
            throw new ArgumentException("Default max activations must be at least 1.", nameof(defaultMaxActivations));
        }

        var product = new SoftwareProduct
        {
            Id = id,
            Name = name.Trim(),
            Vendor = vendor.Trim(),
            CurrentVersion = string.IsNullOrWhiteSpace(currentVersion) ? null : currentVersion.Trim(),
            DefaultLicenseModel = defaultLicenseModel,
            DefaultMaxActivations = defaultMaxActivations,
        };

        var audit = Audit(actor, id, "Updated");
        audit.DetailsJson = JsonSerializer.Serialize(new { name = product.Name });
        await store.UpdateAsync(product, audit, ct);
    }

    /// <exception cref="ArgumentException"><paramref name="actor"/> is null/blank.</exception>
    /// <exception cref="ProductInUseException">The product still has at least one license.</exception>
    public async Task DeleteAsync(Guid id, string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var audit = Audit(actor, id, "Deleted");
        await store.DeleteAsync(id, audit, ct);
    }

    private static AuditLogEntry Audit(string actor, Guid productId, string action) => new()
    {
        Id = Guid.NewGuid(),
        Actor = actor,
        EntityType = "SoftwareProduct",
        EntityId = productId.ToString(),
        Action = action,
    };
}
