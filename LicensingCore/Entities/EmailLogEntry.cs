namespace LicensingCore.Entities;

/// <summary>
/// Audit trail for every email sent (test or license key). Stores recipient domain, status,
/// email type, timestamp, and a sanitized error message on failure. Never stores SMTP
/// passwords, private key material, or the full recipient address.
/// </summary>
public class EmailLogEntry
{
    public Guid Id { get; set; }

    /// <summary>
    /// Domain portion of the recipient address (e.g. "example.com"), or "unknown" if parsing failed.
    /// Never the full address — the domain is enough for ops triage without PII exposure.
    /// </summary>
    public required string RecipientDomain { get; set; }

    /// <summary>Outcome: "Success" or the sanitized failure reason.</summary>
    public required string Status { get; set; }

    /// <summary>Category: "Test" or "LicenseKey".</summary>
    public required string EmailType { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
