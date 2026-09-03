using LicensingCore.Entities;

namespace LicensingAdmin.Licensing;

/// <summary>
/// Persistence boundary for <see cref="LicenseIssuanceService"/>: a uniqueness probe for
/// generated keys plus a single atomic write of the (optional) new product, the license
/// and its audit entry.
/// </summary>
public interface ILicenseStore
{
    /// <summary>Returns <c>true</c> when a license row already carries <paramref name="licenseKey"/>.</summary>
    Task<bool> LicenseKeyExistsAsync(string licenseKey, CancellationToken ct = default);

    /// <summary>
    /// Persists <paramref name="license"/> and <paramref name="audit"/> together, plus
    /// <paramref name="newProduct"/> when it is not <c>null</c>, in one transaction.
    /// </summary>
    Task AddAsync(SoftwareProduct? newProduct, License license, AuditLogEntry audit, CancellationToken ct = default);
}
