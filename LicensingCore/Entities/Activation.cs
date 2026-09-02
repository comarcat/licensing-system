namespace LicensingCore.Entities;

/// <summary>
/// One installed copy of a licensed product on one machine. A hardware-mismatch
/// against the license's existing activation creates a *new* Activation row in
/// PendingReview status rather than mutating the original.
/// </summary>
public class Activation
{
    public Guid Id { get; set; }

    public Guid LicenseId { get; set; }
    public License License { get; set; } = null!;

    /// <summary>GUID generated and owned by the activation DLL for this install.</summary>
    public Guid InstallGuid { get; set; }

    // --- Hardware fingerprint (the 4 identifiers used for same-machine matching) ---
    public required string CpuId { get; set; }
    public required string MotherboardSerial { get; set; }
    public required string TpmId { get; set; }
    public required string MacAddressPrimary { get; set; }

    // --- Informational environment data (for the dashboard / market breakdown) ---
    public string? OsType { get; set; }
    public string? OsVersion { get; set; }
    public string? CpuModel { get; set; }
    public int? RamGb { get; set; }

    public bool IsVm { get; set; }
    /// <summary>Comma-separated or short JSON list of which VM signals fired
    /// (e.g. "hypervisor_bit,host_cpu_mismatch"). Informational only, not auto-blocking.</summary>
    public string? VmSignals { get; set; }

    public ActivationStatus Status { get; set; } = ActivationStatus.PendingReview;

    public DateTime FirstActivatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastCheckinAtUtc { get; set; }

    /// <summary>Set when Status becomes PendingReview: FirstActivatedAtUtc + 15 days.
    /// Null once resolved (approved/rejected) or if never flagged.</summary>
    public DateTime? ReviewDeadlineUtc { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public string? ReviewedBy { get; set; }
    public string? ReviewNotes { get; set; }
}
