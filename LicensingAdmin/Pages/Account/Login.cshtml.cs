using System.ComponentModel.DataAnnotations;
using LicensingAdmin.Auth;
using LicensingCore.Data;
using LicensingCore.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LicensingAdmin.Pages.Account;

/// <summary>
/// Email/password sign-in for the admin panel. <see cref="AllowAnonymous"/> so the
/// authenticated-user <c>FallbackPolicy</c> does not block the page that grants the
/// session. Every failure mode collapses to one generic message
/// (<see cref="GenericError"/>) so the form never tells an attacker whether the email
/// exists, the password was wrong, or the account is disabled — matching the
/// non-distinguishing contract of <see cref="AdminCredentialService.ValidateAsync"/>.
/// </summary>
[AllowAnonymous]
public class LoginModel(AdminCredentialService credentials, IDbContextFactory<AppDbContext> dbFactory)
    : PageModel
{
    /// <summary>Shown for any unsuccessful sign-in, regardless of the underlying cause.</summary>
    public const string GenericError = "Correo o contraseña no válidos.";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Local path to return to after a successful sign-in; never an absolute or protocol-relative URL.</summary>
    public string ReturnUrl { get; set; } = "/";

    /// <summary><c>null</c> until a sign-in attempt fails, then <see cref="GenericError"/>.</summary>
    public string? Error { get; private set; }

    public void OnGet(string? returnUrl = null) => ReturnUrl = Local(returnUrl);

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        var target = Local(returnUrl);
        ReturnUrl = target;

        if (!ModelState.IsValid)
        {
            Error = GenericError;
            return Page();
        }

        var principal = await credentials.ValidateAsync(
            Input.Email, Input.Password, HttpContext.RequestAborted);
        if (principal is null)
        {
            Error = GenericError;
            return Page();
        }

        await RecordLoginAsync(Input.Email, HttpContext.RequestAborted);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return LocalRedirect(target);
    }

    // Stamp LastLoginAtUtc and drop an audit row. The principal is already built, so a
    // race here (row deactivated between ValidateAsync and now) is caught on the next
    // request by OnValidatePrincipal — this write is best-effort bookkeeping.
    private async Task RecordLoginAsync(string email, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.AdminUsers
            .OrderBy(u => u.Id)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalized, ct);
        if (user is null)
        {
            return;
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Actor = user.Email,
            EntityType = "AdminUser",
            EntityId = user.Id.ToString(),
            Action = "Login",
        });
        await db.SaveChangesAsync(ct);
    }

    // Url.IsLocalUrl rejects absolute URLs and "//host" protocol-relative URLs, so a
    // crafted ?returnUrl= cannot bounce a freshly signed-in admin off-site.
    private string Local(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }
}
