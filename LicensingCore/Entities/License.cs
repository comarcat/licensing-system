namespace LicensingCore.Entities;

/// <summary>
/// A single issued license key. ModelFlags/MaxActivations are copied from the
/// SoftwareProduct at issue time (a "snapshot") so later changes to the product's
/// defaults never retroactively change keys that are already out in the world.
/// </summary>
public class License
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }
    public SoftwareProduct Product { get; set; } = null!;

    /// <summary>Human-typeable key, format: XXXX-XXXXX-XXXX-XXXX-XXXX-XXXX-XX.</summary>
    public required string LicenseKey { get; set; }

    /// <summary>License model(s) this key was issued under (frozen at issue time).</summary>
    public LicenseModel ModelSnapshot { get; set; }

    public int MaxActivations { get; set; } = 5;

    /// <summary>Only meaningful when ModelSnapshot includes Subscription.</summary>
    public DateTime? SubscriptionExpiryUtc { get; set; }

    public LicenseStatus Status { get; set; } = LicenseStatus.Active;

    /// <summary>Signature over the key's canonical fields (RSA/ECDSA), so the DLL
    /// can validate authenticity offline before ever calling the API.</summary>
    public required byte[] Signature { get; set; }

    public string? CustomerEmail { get; set; }
    public string? CustomerName { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedReason { get; set; }

    /// <summary>
    /// Soft-delete: hidden from the default Licenses list and excluded from dashboard
    /// counts, but never physically removed — activation history, audit trail and
    /// referential integrity (Activations.LicenseId, AuditLogEntries) all stay intact.
    /// Independent of <see cref="Status"/>: an archived license keeps whatever status it
    /// had (Active/Revoked/Expired) so unarchiving restores it exactly as it was.
    /// </summary>
    public bool IsArchived { get; set; }

    public ICollection<Activation> Activations { get; set; } = new List<Activation>();
}
