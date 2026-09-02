namespace LicensingApi.Dtos;

/// <summary>
/// Fixed set of outcome codes for /activate and /checkin. The client (DLL) switches
/// on this rather than parsing free-text messages. Unrecognized codes (e.g. a future
/// server version's client talking to an older API, or vice versa) MUST make the
/// client fail safe — keep last-known local status — rather than fail open.
/// </summary>
public enum ResultCode
{
    // Success outcomes
    Activated,
    PendingReview,
    Renewed,          // checkin: still valid, file refreshed
    Locked,            // checkin: grace period ended

    // Failure outcomes
    InvalidKeyFormat,
    LicenseNotFound,
    LicenseExpired,
    LicenseRevoked,
    MaxActivationsReached,
    InstallGuidMismatch,
    ActivationNotFound,   // checkin: activationId unknown
    RateLimited,
    ServerError,
}
