namespace LicensingCore.Entities;

/// <summary>
/// Which licensing model(s) apply. Combinable — e.g. Machine | Subscription.
/// Stored as an integer bitmask in PostgreSQL.
/// </summary>
[Flags]
public enum LicenseModel
{
    None = 0,
    Machine = 1 << 0,
    User = 1 << 1,
    Floating = 1 << 2,
    Subscription = 1 << 3,
}

public enum LicenseStatus
{
    Active = 0,
    Revoked = 1,
    Expired = 2,
}

public enum ActivationStatus
{
    PendingReview = 0,
    Approved = 1,
    Rejected = 2,
    Revoked = 3,
}

public enum AdminRole
{
    SuperAdmin = 0,
    SupportStaff = 1,
    ReadOnlyViewer = 2,
}

public enum SmtpEncryption
{
    None = 0,
    StartTls = 1,
    ImplicitTls = 2,
}

public enum SmtpAuthType
{
    Basic = 0,
    ApiKey = 1,
}

public enum NotificationTestStatus
{
    NeverTested = 0,
    Success = 1,
    Failed = 2,
}
