namespace LicensingCore.Entities;

/// <summary>
/// Single-row (in practice) SMTP configuration edited from the admin
/// Notification Settings screen, plus per-event on/off toggles and the
/// result of the last "Send Test Email" action.
/// </summary>
public class NotificationConfig
{
    public Guid Id { get; set; }

    public required string SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public SmtpEncryption Encryption { get; set; } = SmtpEncryption.StartTls;
    public SmtpAuthType AuthType { get; set; } = SmtpAuthType.Basic;

    public required string Username { get; set; }

    /// <summary>Encrypted at rest (DPAPI / ASP.NET Data Protection), never logged.</summary>
    public required byte[] PasswordEncrypted { get; set; }

    public required string FromAddress { get; set; }

    public NotificationTestStatus LastTestStatus { get; set; } = NotificationTestStatus.NeverTested;
    public DateTime? LastTestAtUtc { get; set; }

    /// <summary>Per-event toggles, e.g. {"PendingReview":{"email":true,"inApp":true}, ...}.
    /// Kept as JSON rather than a separate table since the event list evolves independently
    /// of the schema.</summary>
    public string EventTogglesJson { get; set; } = "{}";

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
