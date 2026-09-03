using LicensingCore.Entities;

namespace LicensingAdmin.Licensing;

/// <summary>
/// Input for <see cref="LicenseIssuanceService.IssueAsync"/>.
/// </summary>
/// <remarks>
/// Product resolution: when <see cref="ExistingProductId"/> is set the license is
/// attached to that product and the <c>NewProduct*</c> fields are ignored; when it is
/// <c>null</c> a fresh <see cref="SoftwareProduct"/> is built from the <c>NewProduct*</c>
/// fields and persisted alongside the license.
/// </remarks>
public sealed record LicenseIssuanceRequest
{
    /// <summary>Existing product to issue against. When set, the <c>NewProduct*</c> fields are ignored.</summary>
    public Guid? ExistingProductId { get; init; }

    /// <summary>Name for the product to create (only when <see cref="ExistingProductId"/> is <c>null</c>).</summary>
    public string? NewProductName { get; init; }

    /// <summary>Vendor for the product to create (only when <see cref="ExistingProductId"/> is <c>null</c>).</summary>
    public string? NewProductVendor { get; init; }

    /// <summary>Optional current version for the product to create.</summary>
    public string? NewProductVersion { get; init; }

    /// <summary>License model flag(s) frozen onto the issued key.</summary>
    public LicenseModel Model { get; init; }

    /// <summary>Maximum activations frozen onto the issued key.</summary>
    public int MaxActivations { get; init; }

    /// <summary>Subscription expiry (only meaningful when <see cref="Model"/> includes <see cref="LicenseModel.Subscription"/>).</summary>
    public DateTime? SubscriptionExpiryUtc { get; init; }

    /// <summary>Optional customer email recorded on the license.</summary>
    public string? CustomerEmail { get; init; }

    /// <summary>Optional customer name recorded on the license.</summary>
    public string? CustomerName { get; init; }

    /// <summary>Email of the admin performing the issuance; written to <see cref="AuditLogEntry.Actor"/>.</summary>
    public required string IssuedBy { get; init; }
}
