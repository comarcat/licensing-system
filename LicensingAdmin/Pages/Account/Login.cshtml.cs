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
public class LoginModel(
    AdminCredentialService credentials,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<LoginModel> logger) : PageModel
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

    // Stamp LastLoginAtUtc and drop an audit row. Best-effort by contract: the principal
    // is already built, so a transient DB failure here must not deny a login backed by
    // valid credentials — it is logged for ops and swallowed. A row that vanished between
    // ValidateAsync and now (concurrent delete) is caught next request by OnValidatePrincipal.
    private async Task RecordLoginAsync(string email, CancellationToken ct)
    {
        try
        {
            var normalized = email.Trim().ToLowerInvariant();
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var user = await db.AdminUsers
                .OrderBy(u => u.Id)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalized, ct);
            if (user is null)
            {
                logger.LogWarning(
                    "Login bookkeeping: no admin_users row for a just-authenticated principal; skipping audit stamp.");
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "Login bookkeeping (LastLoginAtUtc + audit row) failed; sign-in proceeds.");
        }
    }

    // Url.IsLocalUrl only inspects the first two characters, so a returnUrl with an
    // embedded tab/newline ("/\t/evil.com") slips through and a browser re-reads it as a
    // protocol-relative "//evil.com". Reject any control or whitespace character first,
    // then fall back to IsLocalUrl for absolute / "//host" / "/\host" forms.
    private string Local(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl))
        {
            return "/";
        }

        foreach (var c in returnUrl)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c))
            {
                return "/";
            }
        }

        return Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
    }

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
