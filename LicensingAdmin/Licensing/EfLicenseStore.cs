using LicensingCore.Data;
using LicensingCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace LicensingAdmin.Licensing;

/// <summary>
/// EF Core-backed <see cref="ILicenseStore"/>. Each call takes a short-lived context from
/// the registered <see cref="IDbContextFactory{TContext}"/> so it is safe to use from
/// Blazor components regardless of their render lifetime.
/// </summary>
public sealed class EfLicenseStore(IDbContextFactory<AppDbContext> factory) : ILicenseStore
{
    /// <inheritdoc />
    public async Task<bool> LicenseKeyExistsAsync(string licenseKey, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Licenses.AnyAsync(l => l.LicenseKey == licenseKey, ct);
    }

    /// <inheritdoc />
    public async Task AddAsync(SoftwareProduct? newProduct, License license, AuditLogEntry audit, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (newProduct is not null)
        {
            db.SoftwareProducts.Add(newProduct);
        }

        db.Licenses.Add(license);
        db.AuditLogEntries.Add(audit);
        await db.SaveChangesAsync(ct);
    }
}
