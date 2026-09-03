using System.Security.Claims;
using LicensingAdmin.Auth;
using LicensingCore.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// Covers <see cref="AuthPolicies.AddAdminAuthorization"/>: the four named policies,
/// the role matrix (ReadOnlyViewer / SupportStaff / SuperAdmin / anonymous) and the
/// authenticated-user fallback policy. All deterministic — no database, no host.
/// </summary>
public class AuthPoliciesTests
{
    private readonly IAuthorizationService _authz;
    private readonly IAuthorizationPolicyProvider _policyProvider;

    public AuthPoliciesTests()
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddAdminAuthorization()
            .BuildServiceProvider();

        _authz = sp.GetRequiredService<IAuthorizationService>();
        _policyProvider = sp.GetRequiredService<IAuthorizationPolicyProvider>();
    }

    // A non-null authenticationType makes ClaimsIdentity.IsAuthenticated true.
    private static ClaimsPrincipal InRole(string role) => InRoles(role);

    // Authenticated identity carrying zero or more ClaimTypes.Role claims.
    private static ClaimsPrincipal InRoles(params string[] roles) =>
        new(new ClaimsIdentity(
            roles.Select(r => new Claim(ClaimTypes.Role, r)),
            authenticationType: "TestAuth"));

    // Authenticated identity with no role claim at all.
    private static ClaimsPrincipal AuthenticatedNoRole() =>
        new(new ClaimsIdentity(Array.Empty<Claim>(), authenticationType: "TestAuth"));

    // Authenticated identity whose role value sits under a non-standard claim type
    // ("role" rather than the ClaimsIdentity default of ClaimTypes.Role).
    private static ClaimsPrincipal WithRoleUnderType(string claimType, string role) =>
        new(new ClaimsIdentity(
            new[] { new Claim(claimType, role) },
            authenticationType: "TestAuth"));

    // No authenticationType => IsAuthenticated is false.
    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private async Task<bool> Allowed(ClaimsPrincipal user, string policy) =>
        (await _authz.AuthorizeAsync(user, resource: null, policy)).Succeeded;

    [Fact]
    public void Constants_AreExposed()
    {
        Assert.Equal("ViewerAccess", AuthPolicies.ViewerAccess);
        Assert.Equal("ReviewAccess", AuthPolicies.ReviewAccess);
        Assert.Equal("IssueAccess", AuthPolicies.IssueAccess);
        Assert.Equal("AdminUserAccess", AuthPolicies.AdminUserAccess);
    }

    [Fact]
    public async Task ReadOnlyViewer_SatisfiesViewer_FailsTheRest()
    {
        var user = InRole(nameof(AdminRole.ReadOnlyViewer));

        Assert.True(await Allowed(user, AuthPolicies.ViewerAccess));
        Assert.False(await Allowed(user, AuthPolicies.ReviewAccess));
        Assert.False(await Allowed(user, AuthPolicies.IssueAccess));
        Assert.False(await Allowed(user, AuthPolicies.AdminUserAccess));
    }

    [Fact]
    public async Task SupportStaff_SatisfiesViewerReviewIssue_FailsAdminUser()
    {
        var user = InRole(nameof(AdminRole.SupportStaff));

        Assert.True(await Allowed(user, AuthPolicies.ViewerAccess));
        Assert.True(await Allowed(user, AuthPolicies.ReviewAccess));
        Assert.True(await Allowed(user, AuthPolicies.IssueAccess));
        Assert.False(await Allowed(user, AuthPolicies.AdminUserAccess));
    }

    [Fact]
    public async Task SuperAdmin_SatisfiesAllFour()
    {
        var user = InRole(nameof(AdminRole.SuperAdmin));

        Assert.True(await Allowed(user, AuthPolicies.ViewerAccess));
        Assert.True(await Allowed(user, AuthPolicies.ReviewAccess));
        Assert.True(await Allowed(user, AuthPolicies.IssueAccess));
        Assert.True(await Allowed(user, AuthPolicies.AdminUserAccess));
    }

    [Fact]
    public async Task Anonymous_FailsAllFour_AndFallbackPolicy()
    {
        Assert.False(await Allowed(Anonymous(), AuthPolicies.ViewerAccess));
        Assert.False(await Allowed(Anonymous(), AuthPolicies.ReviewAccess));
        Assert.False(await Allowed(Anonymous(), AuthPolicies.IssueAccess));
        Assert.False(await Allowed(Anonymous(), AuthPolicies.AdminUserAccess));

        var fallback = await _policyProvider.GetFallbackPolicyAsync();
        Assert.NotNull(fallback);
        Assert.False((await _authz.AuthorizeAsync(Anonymous(), null, fallback!)).Succeeded);
        Assert.True((await _authz.AuthorizeAsync(
            InRole(nameof(AdminRole.SuperAdmin)), null, fallback!)).Succeeded);
    }

    // --- Gap coverage added for E1-T8 review -------------------------------------
    // The criteria only pin the role->policy matrix for the three known AdminRole
    // names + anonymous. The tests below fix behaviour for the threat-model-relevant
    // cases the criteria leave implicit: an authenticated principal whose role claim
    // is unknown / absent / mis-typed, a multi-role principal, role case-sensitivity,
    // and the actual registration of the four named policies.

    [Fact]
    public async Task AuthenticatedUnknownRole_SatisfiesViewerAndFallback_FailsOtherThree()
    {
        // RequireAuthenticatedUser never inspects the role, so a garbage role still
        // clears ViewerAccess and the fallback; RequireRole gates the other three.
        var user = InRole("Nonsense");

        Assert.True(await Allowed(user, AuthPolicies.ViewerAccess));
        Assert.False(await Allowed(user, AuthPolicies.ReviewAccess));
        Assert.False(await Allowed(user, AuthPolicies.IssueAccess));
        Assert.False(await Allowed(user, AuthPolicies.AdminUserAccess));

        var fallback = await _policyProvider.GetFallbackPolicyAsync();
        Assert.True((await _authz.AuthorizeAsync(user, null, fallback!)).Succeeded);
    }

    [Fact]
    public async Task AuthenticatedNoRoleClaim_SatisfiesViewer_FailsOtherThree()
    {
        var user = AuthenticatedNoRole();

        Assert.True(await Allowed(user, AuthPolicies.ViewerAccess));
        Assert.False(await Allowed(user, AuthPolicies.ReviewAccess));
        Assert.False(await Allowed(user, AuthPolicies.IssueAccess));
        Assert.False(await Allowed(user, AuthPolicies.AdminUserAccess));
    }

    [Fact]
    public async Task MultipleRoles_ViewerPlusSuperAdmin_SatisfiesAllFour()
    {
        // A principal can legitimately carry several role claims; any matching role
        // in the RequireRole set is enough.
        var user = InRoles(nameof(AdminRole.ReadOnlyViewer), nameof(AdminRole.SuperAdmin));

        Assert.True(await Allowed(user, AuthPolicies.ViewerAccess));
        Assert.True(await Allowed(user, AuthPolicies.ReviewAccess));
        Assert.True(await Allowed(user, AuthPolicies.IssueAccess));
        Assert.True(await Allowed(user, AuthPolicies.AdminUserAccess));
    }

    [Fact]
    public async Task RoleMatch_IsCaseSensitive_ExactNameRequired()
    {
        // RequireRole compares the role claim value with StringComparison.Ordinal, so
        // the sign-in code MUST issue the claim as exactly nameof(AdminRole.SuperAdmin).
        Assert.False(await Allowed(InRole("superadmin"), AuthPolicies.AdminUserAccess));
        Assert.False(await Allowed(InRole("SUPERADMIN"), AuthPolicies.AdminUserAccess));
        Assert.False(await Allowed(InRole("SuperADMIN"), AuthPolicies.AdminUserAccess));
        Assert.True(await Allowed(
            InRole(nameof(AdminRole.SuperAdmin)), AuthPolicies.AdminUserAccess));
    }

    [Fact]
    public async Task RoleValueUnderNonStandardClaimType_IsNotHonoured()
    {
        // ClaimsIdentity.RoleClaimType defaults to ClaimTypes.Role. A value parked
        // under the short "role" type is invisible to RequireRole; the principal is
        // still authenticated, so only ViewerAccess passes.
        var user = WithRoleUnderType("role", nameof(AdminRole.SuperAdmin));

        Assert.True(await Allowed(user, AuthPolicies.ViewerAccess));
        Assert.False(await Allowed(user, AuthPolicies.ReviewAccess));
        Assert.False(await Allowed(user, AuthPolicies.IssueAccess));
        Assert.False(await Allowed(user, AuthPolicies.AdminUserAccess));
    }

    [Fact]
    public async Task AllFourNamedPolicies_AreRegistered_UnknownNameIsNull()
    {
        Assert.NotNull(await _policyProvider.GetPolicyAsync(AuthPolicies.ViewerAccess));
        Assert.NotNull(await _policyProvider.GetPolicyAsync(AuthPolicies.ReviewAccess));
        Assert.NotNull(await _policyProvider.GetPolicyAsync(AuthPolicies.IssueAccess));
        Assert.NotNull(await _policyProvider.GetPolicyAsync(AuthPolicies.AdminUserAccess));
        Assert.Null(await _policyProvider.GetPolicyAsync("NoSuchPolicy"));
    }
}
