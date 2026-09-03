using LicensingAdmin.Auth;
using LicensingCore.Entities;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Xunit;

namespace LicensingSystem.Tests;

public class AdminCredentialServiceTests
{
    // In-memory IAdminUserLookup: returns a fixed user (or null) and records the
    // last email it was asked for, so tests can assert the lookup was consulted.
    private sealed class FakeAdminUserLookup(AdminUser? user) : IAdminUserLookup
    {
        public string? LastEmailQueried;

        public Task<AdminUser?> FindByEmailAsync(string email, CancellationToken ct = default)
        {
            LastEmailQueried = email;
            return Task.FromResult(user);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Real hasher at the default iteration count. The SAME construction is used to
    // seed a user's PasswordHash and inside the service under test, so Verify() sees
    // a matching cost parameter.
    private static PasswordHasherService Hasher() => new(new PasswordHasher<AdminUser>());

    private static AdminUser ActiveUser(string pwd, AdminRole role = AdminRole.SupportStaff)
    {
        var h = Hasher();
        return new AdminUser
        {
            Id = Guid.NewGuid(),
            Email = "admin@vendor.test",
            PasswordHash = h.Hash(pwd),
            Role = role,
            IsActive = true,
        };
    }

    [Fact]
    public void BuildPrincipal_CarriesNameAndRoleClaims()
    {
        var p = AdminCredentialService.BuildPrincipal(new AdminUser
        {
            Email = "a@b.c",
            PasswordHash = "x",
            Role = AdminRole.SuperAdmin,
        });

        Assert.True(p.Identity!.IsAuthenticated);
        Assert.Equal("a@b.c", p.FindFirst(ClaimTypes.Name)!.Value);
        Assert.Equal("SuperAdmin", p.FindFirst(ClaimTypes.Role)!.Value);
        Assert.True(p.IsInRole("SuperAdmin"));
    }

    [Fact]
    public async Task ValidateAsync_UnknownEmail_ReturnsNull()
    {
        var fake = new FakeAdminUserLookup(null);
        var svc = new AdminCredentialService(fake, Hasher());

        var result = await svc.ValidateAsync("nobody@x", "pw", Ct);

        Assert.Null(result);
        Assert.Equal("nobody@x", fake.LastEmailQueried);
    }

    [Fact]
    public async Task ValidateAsync_WrongPassword_ReturnsNull()
    {
        var user = ActiveUser("right");
        var svc = new AdminCredentialService(new FakeAdminUserLookup(user), Hasher());

        var result = await svc.ValidateAsync(user.Email, "wrong", Ct);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_InactiveUser_ReturnsNull_EvenWithCorrectPassword()
    {
        var user = ActiveUser("pw");
        user.IsActive = false;
        var svc = new AdminCredentialService(new FakeAdminUserLookup(user), Hasher());

        var result = await svc.ValidateAsync(user.Email, "pw", Ct);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_ActiveUserCorrectPassword_ReturnsPrincipalFromBuildPrincipal()
    {
        var user = ActiveUser("pw", AdminRole.SuperAdmin);
        var svc = new AdminCredentialService(new FakeAdminUserLookup(user), Hasher());

        var result = await svc.ValidateAsync(user.Email, "pw", Ct);

        Assert.NotNull(result);
        Assert.Equal(user.Email, result!.FindFirst(ClaimTypes.Name)!.Value);
        Assert.Equal("SuperAdmin", result.FindFirst(ClaimTypes.Role)!.Value);
        Assert.True(result.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task ValidateAsync_NullOrEmptyPassword_ReturnsNull()
    {
        var user = ActiveUser("pw");
        var svc = new AdminCredentialService(new FakeAdminUserLookup(user), Hasher());

        var emptyResult = await Record.ExceptionAsync(async () =>
            Assert.Null(await svc.ValidateAsync(user.Email, "", Ct)));
        Assert.Null(emptyResult);

        var nullResult = await Record.ExceptionAsync(async () =>
            Assert.Null(await svc.ValidateAsync(user.Email, null!, Ct)));
        Assert.Null(nullResult);
    }

    // ---- E2-T1 gap coverage (added by tester) -------------------------------

    // Contract with AuthPolicies.RequireRole(nameof(AdminRole.X)) and IsInRole():
    // the role claim value must be the exact enum name for every defined role.
    [Theory]
    [InlineData(AdminRole.SuperAdmin, "SuperAdmin")]
    [InlineData(AdminRole.SupportStaff, "SupportStaff")]
    [InlineData(AdminRole.ReadOnlyViewer, "ReadOnlyViewer")]
    public void BuildPrincipal_RoleClaim_IsExactEnumName_AndIsInRoleMatches(AdminRole role, string expected)
    {
        var p = AdminCredentialService.BuildPrincipal(new AdminUser
        {
            Email = "a@b.c",
            PasswordHash = "x",
            Role = role,
        });

        Assert.Equal(expected, p.FindFirst(ClaimTypes.Role)!.Value);
        Assert.True(p.IsInRole(expected));
        Assert.False(p.IsInRole("NotARole"));
    }

    [Fact]
    public void BuildPrincipal_Identity_HasCookieAuthTypeAndIsAuthenticated()
    {
        var p = AdminCredentialService.BuildPrincipal(new AdminUser
        {
            Email = "a@b.c",
            PasswordHash = "x",
            Role = AdminRole.SupportStaff,
        });

        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, p.Identity!.AuthenticationType);
        Assert.False(string.IsNullOrEmpty(p.Identity!.AuthenticationType));
        Assert.True(p.Identity!.IsAuthenticated);
        Assert.True(p.IsInRole("SupportStaff"));
        Assert.False(p.IsInRole("Otro"));
    }

    // Characterization / MODEL GAP (non-blocking for E2-T1): a Role value outside the
    // defined AdminRole set (bad cast, stale row) is stringified verbatim -> "99", which
    // no RequireRole(nameof(...)) policy matches. Documented here so a future change is caught.
    [Fact]
    public void BuildPrincipal_RoleClaim_OutOfRangeEnum_IsNumericString()
    {
        var p = AdminCredentialService.BuildPrincipal(new AdminUser
        {
            Email = "a@b.c",
            PasswordHash = "x",
            Role = (AdminRole)99,
        });

        Assert.Equal("99", p.FindFirst(ClaimTypes.Role)!.Value);
        Assert.False(p.IsInRole("SuperAdmin"));
        Assert.False(p.IsInRole("SupportStaff"));
        Assert.False(p.IsInRole("ReadOnlyViewer"));
    }

    // The service does not guard the email argument itself: it forwards whatever it is
    // M-1 (security-auditor): a null/blank email or a null password fails closed BEFORE the
    // lookup — same as OnValidatePrincipal / AdminAuthStateProvider — and still burns the
    // fixed PBKDF2 cost so a null email is not the fast path.
    [Theory]
    [InlineData(null, "pw")]
    [InlineData("", "pw")]
    [InlineData("   ", "pw")]
    [InlineData("admin@vendor.test", null)]
    public async Task ValidateAsync_MissingEmailOrPassword_ReturnsNull_WithoutHittingLookup(string? email, string? password)
    {
        // A lookup that WOULD return an active user, to prove the guard short-circuits it.
        var fake = new FakeAdminUserLookup(ActiveUser("pw"));
        var svc = new AdminCredentialService(fake, Hasher());

        var ex = await Record.ExceptionAsync(async () =>
            Assert.Null(await svc.ValidateAsync(email!, password!, Ct)));

        Assert.Null(ex);
        Assert.Null(fake.LastEmailQueried);
    }

    // ValidateAsync must not cache: each successful call builds a brand-new principal.
    [Fact]
    public async Task ValidateAsync_ReturnsFreshAuthenticatedPrincipalPerCall()
    {
        var user = ActiveUser("pw", AdminRole.SupportStaff);
        var svc = new AdminCredentialService(new FakeAdminUserLookup(user), Hasher());

        var p1 = await svc.ValidateAsync(user.Email, "pw", Ct);
        var p2 = await svc.ValidateAsync(user.Email, "pw", Ct);

        Assert.NotNull(p1);
        Assert.NotNull(p2);
        Assert.NotSame(p1, p2);
        Assert.NotSame(p1!.Identity, p2!.Identity);
        Assert.True(p1.Identity!.IsAuthenticated);
        Assert.True(p2.Identity!.IsAuthenticated);
    }

    // PasswordHasherService.Verify treats PasswordVerificationResult.SuccessRehashNeeded
    // as success. Seed the stored hash at a LOWER PBKDF2 iteration count than the service
    // hasher so VerifyHashedPassword returns SuccessRehashNeeded; ValidateAsync must still
    // return a principal (a due rehash must not break login). Cheap: verify cost follows
    // the stored hash's embedded (low) iteration count.
    [Fact]
    public async Task ValidateAsync_StoredHashBelowServiceCost_RehashNeeded_StillAuthenticates()
    {
        var lowCost = new PasswordHasher<AdminUser>(
            Options.Create(new PasswordHasherOptions { IterationCount = 1_000 }));
        var user = new AdminUser
        {
            Id = Guid.NewGuid(),
            Email = "rehash@vendor.test",
            PasswordHash = lowCost.HashPassword(
                new AdminUser { Email = "", PasswordHash = "" }, "pw"),
            Role = AdminRole.ReadOnlyViewer,
            IsActive = true,
        };
        var svc = new AdminCredentialService(new FakeAdminUserLookup(user), Hasher());

        var result = await svc.ValidateAsync(user.Email, "pw", Ct);

        Assert.NotNull(result);
        Assert.Equal("ReadOnlyViewer", result!.FindFirst(ClaimTypes.Role)!.Value);
        Assert.True(result.Identity!.IsAuthenticated);

        // Sanity: the real service hasher accepts the low-cost hash (Success/SuccessRehashNeeded).
        Assert.True(Hasher().Verify(user.PasswordHash, "pw"));
    }
}
