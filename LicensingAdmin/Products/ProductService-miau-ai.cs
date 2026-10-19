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
    public Task<IReadOnlyList<SoftwareProduct>> ListAsync(CancellationToken ct = default) => store.ListAsync(includeArchived: false, ct);

    public Task<IReadOnlyList<SoftwareProduct>> ListIncludingArchivedAsync(CancellationToken ct = default) =>
        store.ListAsync(includeArchived: true, ct);

    public Task<int> CountLicensesAsync(Guid productId, CancellationToken ct = default) =>
        store.CountLicensesAsync(productId, ct);

    /// <exception cref="ArgumentException"><paramref name="name"/>, <paramref name="vendor"/> or <paramref name="actor"/> is null/blank.</exception>
    public async Task<SoftwareProduct> CreateAsync(
        string name, string vendor, string? currentVersion, LicenseModel defaultLicenseModel,
        int defaultMaxActivations, IReadOnlyList<string> versionNames, string actor, CancellationToken ct = default)
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

        var versions = versionNames
            .Select(vn => vn.Trim())
            .Where(vn => !string.IsNullOrWhiteSpace(vn))
            .DistinctBy(vn => vn.ToLowerInvariant())
            .Select(vn => new ProductVersion
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                Name = vn,
            })
            .ToList();

        if (versions.Count == 0)
        {
            versions.Add(new ProductVersion
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                Name = string.IsNullOrWhiteSpace(currentVersion) ? "Default" : currentVersion.Trim()
            });
        }

        var versionAudits = versions.Select(v => Audit(actor, v.Id, "Created", "ProductVersion")).ToList();

        await store.AddAsync(product, audit, versions, versionAudits, ct);
        return product;
    }

    public async Task<SoftwareProduct> CreateAsync(
        string name, string vendor, string? currentVersion, LicenseModel defaultLicenseModel,
        int defaultMaxActivations, string actor, CancellationToken ct = default)
    {
        return await CreateAsync(name, vendor, currentVersion, defaultLicenseModel, defaultMaxActivations, new List<string>(), actor, ct);
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
    public async Task ArchiveAsync(Guid id, string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var audit = Audit(actor, id, "Archived");
        await store.ArchiveAsync(id, audit, ct);
    }

    /// <exception cref="ArgumentException"><paramref name="actor"/> is null/blank.</exception>
    /// <exception cref="ProductInUseException">The product still has at least one license.</exception>
    public async Task DeleteAsync(Guid id, string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var audit = Audit(actor, id, "Deleted");
        await store.DeleteAsync(id, audit, ct);
    }

    public Task<IReadOnlyList<ProductVersion>> ListVersionsAsync(Guid productId, CancellationToken ct = default) =>
        store.ListVersionsAsync(productId, ct);

    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="actor"/> is null/blank.</exception>
    public async Task<ProductVersion> CreateVersionAsync(Guid productId, string name, string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var version = new ProductVersion
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Name = name.Trim(),
        };

        var audit = Audit(actor, version.Id, "Created", "ProductVersion");
        audit.DetailsJson = JsonSerializer.Serialize(new { name = version.Name });
        await store.AddVersionAsync(version, audit, ct);
        return version;
    }

    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="actor"/> is null/blank.</exception>
    public async Task UpdateVersionAsync(Guid versionId, string name, string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var version = new ProductVersion
        {
            Id = versionId,
            Name = name.Trim(),
        };

        var audit = Audit(actor, versionId, "Updated", "ProductVersion");
        audit.DetailsJson = JsonSerializer.Serialize(new { name = version.Name });
        await store.UpdateVersionAsync(version, audit, ct);
    }

    /// <exception cref="ArgumentException"><paramref name="actor"/> is null/blank.</exception>
    public async Task DeleteVersionAsync(Guid versionId, string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var audit = Audit(actor, versionId, "Deleted", "ProductVersion");
        await store.DeleteVersionAsync(versionId, audit, ct);
    }

    private static AuditLogEntry Audit(string actor, Guid productId, string action, string entityType = "SoftwareProduct") => new()
    {
        Id = Guid.NewGuid(),
        Actor = actor,
        EntityType = entityType,
        EntityId = productId.ToString(),
        Action = action,
    };
}
