using System.Security.Claims;
using LicensingAdmin.Auth;
using LicensingCore.Entities;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// Covers <see cref="CurrentAdmin.Email"/>: the signed-in admin's email comes from the
/// principal's <see cref="ClaimTypes.Name"/> claim, and every "not really signed in"
/// shape collapses to <c>""</c> so an audit row never gets a null or a stale literal.
/// </summary>
public class CurrentAdminTests
{
    private const string Scheme = "TestCookie";

    private static ClaimsPrincipal Authenticated(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: Scheme));

    [Fact]
    public void Email_authenticated_principal_returns_the_name_claim()
    {
        var principal = Authenticated(
            new Claim(ClaimTypes.Name, "admin@vendor.test"),
            new Claim(ClaimTypes.Role, nameof(AdminRole.SupportStaff)));

        Assert.Equal("admin@vendor.test", CurrentAdmin.Email(principal));
    }

    [Fact]
    public void Email_principal_from_BuildPrincipal_returns_the_admin_email()
    {
        var user = new AdminUser
        {
            Email = "reviewer@vendor.test",
            PasswordHash = "x",
            Role = AdminRole.SupportStaff,
        };

        Assert.Equal("reviewer@vendor.test", CurrentAdmin.Email(AdminCredentialService.BuildPrincipal(user)));
    }

    [Fact]
    public void Email_anonymous_principal_returns_empty_string()
    {
        // No authenticationType -> Identity.IsAuthenticated is false.
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "ghost@vendor.test") }));

        Assert.Equal("", CurrentAdmin.Email(anonymous));
    }

    [Fact]
    public void Email_bare_principal_returns_empty_string()
    {
        Assert.Equal("", CurrentAdmin.Email(new ClaimsPrincipal()));
    }

    [Fact]
    public void Email_null_principal_returns_empty_string()
    {
        Assert.Equal("", CurrentAdmin.Email(null));
    }

    [Fact]
    public void Email_authenticated_without_a_name_claim_returns_empty_string()
    {
        var noName = Authenticated(new Claim(ClaimTypes.Role, nameof(AdminRole.ReadOnlyViewer)));

        Assert.Equal("", CurrentAdmin.Email(noName));
    }
}
