using LicensingCore.Data;
using LicensingCore.Entities;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace LicensingAdmin.Auth;

/// <summary>
/// Looks up an <see cref="AdminUser"/> by email. Abstracted from EF so the credential
/// checks in <see cref="AdminCredentialService"/> (and the revalidation hooks) can be
/// unit tested without a database.
/// </summary>
public interface IAdminUserLookup
{
    /// <summary>
    /// Returns the admin whose email matches <paramref name="email"/> (case-insensitive,
    /// trimmed), or <c>null</c> when there is no such user.
    /// </summary>
    Task<AdminUser?> FindByEmailAsync(string email, CancellationToken ct = default);
}

/// <summary>
/// EF Core implementation of <see cref="IAdminUserLookup"/>. Uses the shared
/// <see cref="AppDbContext"/> via <see cref="IDbContextFactory{TContext}"/> so each
/// call gets a short-lived context. Not unit tested (thin EF adapter).
/// </summary>
public sealed class EfAdminUserLookup(IDbContextFactory<AppDbContext> factory) : IAdminUserLookup
{
    /// <inheritdoc />
    public async Task<AdminUser?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        // OrderBy makes the pick deterministic if two rows differ only by email case
        // (the unique index is case-sensitive; write-side normalisation lands in E2-T3/E2-T7).
        return await db.AdminUsers
            .OrderBy(u => u.Id)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalized, ct);
    }
}

/// <summary>
/// Validates an admin's email/password pair and, on success, builds the
/// <see cref="ClaimsPrincipal"/> that the cookie sign-in persists. All failure modes
/// (unknown email, wrong password, deactivated account) collapse to <c>null</c> so the
/// caller cannot distinguish them, and a fixed-cost dummy verification is run for an
/// unknown email so a missing user is not detectable by response timing (E1-T7 B-1).
/// </summary>
public sealed class AdminCredentialService(IAdminUserLookup lookup, PasswordHasherService hasher)
{
    // Precomputed once per process at the SAME PBKDF2 cost as production (OWASP 2024:
    // IterationCount = 210_000). Verifying the supplied password against this when the
    // email is unknown keeps the "no such user" path as expensive as the "wrong
    // password" path, defeating user-enumeration by timing.
    private static readonly string DummyHash =
        new PasswordHasher<AdminUser>(
            Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }))
        .HashPassword(new AdminUser { Email = "", PasswordHash = "" }, "timing-equalizer");

    /// <summary>
    /// Returns an authenticated <see cref="ClaimsPrincipal"/> when <paramref name="email"/>
    /// maps to an active admin whose stored hash verifies against <paramref name="password"/>;
    /// otherwise <c>null</c>.
    /// </summary>
    public async Task<ClaimsPrincipal?> ValidateAsync(string email, string password, CancellationToken ct = default)
    {
        // Fail closed on a missing email/password the same way the other two revalidation
        // paths (OnValidatePrincipal, AdminAuthStateProvider) do — and keep the fixed cost
        // so a null email is not the fast path.
        if (string.IsNullOrWhiteSpace(email) || password is null)
        {
            _ = hasher.Verify(DummyHash, password ?? string.Empty);
            return null;
        }

        var user = await lookup.FindByEmailAsync(email, ct);
        if (user is null)
        {
            // B-1 (E1-T7): burn the same PBKDF2 cost as a real verify so an unknown
            // email cannot be told apart from a wrong password by timing.
            _ = hasher.Verify(DummyHash, password);
            return null;
        }

        var ok = hasher.Verify(user.PasswordHash, password);
        if (!ok)
        {
            return null;
        }

        if (!user.IsActive)
        {
            // Correct password but the account has been disabled -> no principal.
            return null;
        }

        return BuildPrincipal(user);
    }

    /// <summary>
    /// Builds the cookie principal for <paramref name="u"/>: a name claim carrying the
    /// email and a role claim carrying the <see cref="AdminRole"/> name. The identity uses
    /// the default role claim type (<see cref="ClaimTypes.Role"/>), which is what the
    /// named policies' <c>RequireRole(nameof(AdminRole.X))</c> and
    /// <see cref="ClaimsPrincipal.IsInRole(string)"/> match against. A non-null
    /// authentication type makes <c>Identity.IsAuthenticated</c> true.
    /// </summary>
    public static ClaimsPrincipal BuildPrincipal(AdminUser u)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.Name, u.Email),
                new Claim(ClaimTypes.Role, u.Role.ToString()),
            },
            authenticationType: CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }
}
