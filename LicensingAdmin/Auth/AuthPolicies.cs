using LicensingCore.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace LicensingAdmin.Auth;

/// <summary>
/// The authorization policies for the admin app, keyed by the four capability
/// levels the UI gates on, plus the <see cref="AddAdminAuthorization"/> wiring.
/// Role membership comes from the cookie principal's role claims
/// (<see cref="AdminRole"/> names), issued at sign-in.
/// </summary>
public static class AuthPolicies
{
    /// <summary>Any authenticated admin (read-only dashboards, license lists).</summary>
    public const string ViewerAccess = "ViewerAccess";

    /// <summary>Reviewing / acting on activation requests — support staff and up.</summary>
    public const string ReviewAccess = "ReviewAccess";

    /// <summary>Issuing / revoking licenses — support staff and up.</summary>
    public const string IssueAccess = "IssueAccess";

    /// <summary>Managing admin users themselves — super admins only.</summary>
    public const string AdminUserAccess = "AdminUserAccess";

    /// <summary>
    /// Registers <see cref="IAuthorizationService"/> / <see cref="IAuthorizationPolicyProvider"/>
    /// via the full <c>Microsoft.AspNetCore.App</c> overload and defines the four named
    /// policies above. Also sets a <see cref="AuthorizationOptions.FallbackPolicy"/> that
    /// requires an authenticated user, so any endpoint/route without an explicit
    /// <c>[Authorize]</c> still demands a signed-in admin.
    /// </summary>
    /// <param name="services">The DI service collection to add the registration to.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddAdminAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(ViewerAccess, p => p.RequireAuthenticatedUser());

            options.AddPolicy(ReviewAccess, p => p.RequireRole(
                nameof(AdminRole.SupportStaff), nameof(AdminRole.SuperAdmin)));

            options.AddPolicy(IssueAccess, p => p.RequireRole(
                nameof(AdminRole.SupportStaff), nameof(AdminRole.SuperAdmin)));

            options.AddPolicy(AdminUserAccess, p => p.RequireRole(
                nameof(AdminRole.SuperAdmin)));

            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }
}
