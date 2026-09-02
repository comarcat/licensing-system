namespace LicensingCore.Entities;

/// <summary>
/// Immutable record of every state-changing action (license created, activation
/// approved/rejected, license revoked, notification config changed, etc.).
/// Backs both compliance needs and the piracy/validation reports.
/// </summary>
public class AuditLogEntry
{
    public Guid Id { get; set; }

    /// <summary>Admin email, or "system" for automated actions (e.g. auto-lock after grace).</summary>
    public required string Actor { get; set; }

    public required string EntityType { get; set; } // "License", "Activation", "NotificationConfig", ...
    public required string EntityId { get; set; }

    public required string Action { get; set; } // "Created", "Approved", "Rejected", "Revoked", "Updated", ...

    /// <summary>Free-form JSON with before/after or extra context.</summary>
    public string? DetailsJson { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
