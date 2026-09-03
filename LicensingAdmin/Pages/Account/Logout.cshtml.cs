using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicensingAdmin.Pages.Account;

/// <summary>
/// Clears the admin cookie and returns to the login page. POST-only (the GET just
/// renders the confirm button) so a cross-site GET cannot silently sign an admin out.
/// <see cref="AllowAnonymous"/> so an admin whose cookie was already rejected can still
/// reach it instead of being redirected to login by the fallback policy.
/// </summary>
[AllowAnonymous]
public class LogoutModel : PageModel
{
    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Account/Login");
    }
}
