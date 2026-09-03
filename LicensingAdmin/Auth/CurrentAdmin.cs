using System.Security.Claims;

namespace LicensingAdmin.Auth;

/// <summary>
/// Reads the signed-in admin's identity off the cookie <see cref="ClaimsPrincipal"/>.
/// Static and side-effect free so a page or service can attribute an action to the
/// person who took it without depending on HTTP-context plumbing.
/// </summary>
public static class CurrentAdmin
{
    /// <summary>
    /// The admin's email — the principal's <see cref="ClaimTypes.Name"/> claim, set by
    /// <c>AdminCredentialService.BuildPrincipal</c> at sign-in. Returns <c>""</c> for an
    /// anonymous principal, a <c>null</c> principal, or one carrying no name claim, so a
    /// caller never writes a null or a misleading literal into an audit row.
    /// </summary>
    public static string Email(ClaimsPrincipal? principal) =>
        principal?.Identity?.IsAuthenticated == true
            ? principal.FindFirstValue(ClaimTypes.Name) ?? ""
            : "";
}
