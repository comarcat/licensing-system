using LicensingAdmin.Auth;
using LicensingAdmin.Startup;
using LicensingCore.Entities;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// E2-T3 — extra normalisation + robustness coverage for
/// <see cref="AdminSeeder.BuildSuperAdmin"/>. Contract: returns an
/// <see cref="AdminRole.SuperAdmin"/>, <c>IsActive == true</c>, a real (verifiable,
/// non-"pw") hash, <c>Email = email.Trim().ToLowerInvariant()</c>, <c>Id = Guid.NewGuid()</c>.
/// </summary>
public class AdminSeederBuildSuperAdminEdgeTests
{
    private static PasswordHasherService Hasher() => new(new PasswordHasher<AdminUser>());

    // ---- email normalisation: trim then ToLowerInvariant --------------------------

    [Theory]
    [InlineData("user@vendor.test", "user@vendor.test")]      // already lower-case -> unchanged
    [InlineData("User123@Vendor.TEST", "user123@vendor.test")] // internal caps + digits
    [InlineData("  MiXeD@Case.IO  ", "mixed@case.io")]         // padded + mixed case
    [InlineData("ADMIN@EXAMPLE.COM", "admin@example.com")]
    public void BuildSuperAdmin_normalises_email_trim_then_lowerinvariant(string configured, string expected)
    {
        Assert.Equal(expected, AdminSeeder.BuildSuperAdmin(configured, "pw", Hasher()).Email);
    }

    [Fact]
    public void BuildSuperAdmin_email_normalisation_is_idempotent()
    {
        var once = AdminSeeder.BuildSuperAdmin("  Admin@B.C  ", "pw", Hasher()).Email;
        var twice = AdminSeeder.BuildSuperAdmin(once, "pw", Hasher()).Email;

        Assert.Equal("admin@b.c", once);
        Assert.Equal(once, twice);
    }

    // ---- entity defaults ---------------------------------------------------------

    [Fact]
    public void BuildSuperAdmin_initialises_CreatedAtUtc_to_a_recent_utc_instant()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);
        var user = AdminSeeder.BuildSuperAdmin("a@b.c", "pw", Hasher());
        var after = DateTime.UtcNow.AddSeconds(5);

        Assert.InRange(user.CreatedAtUtc, before, after);
        Assert.Equal(DateTimeKind.Utc, user.CreatedAtUtc.Kind);
        Assert.Null(user.LastLoginAtUtc);
    }

    [Fact]
    public void BuildSuperAdmin_sets_role_superadmin_and_active()
    {
        var user = AdminSeeder.BuildSuperAdmin("a@b.c", "pw", Hasher());

        Assert.Equal(AdminRole.SuperAdmin, user.Role);
        Assert.True(user.IsActive);
    }

    // ---- id + hash randomness --------------------------------------------------

    [Fact]
    public void BuildSuperAdmin_two_calls_produce_distinct_non_empty_ids()
    {
        var a = AdminSeeder.BuildSuperAdmin("a@b.c", "pw", Hasher());
        var b = AdminSeeder.BuildSuperAdmin("a@b.c", "pw", Hasher());

        Assert.NotEqual(Guid.Empty, a.Id);
        Assert.NotEqual(Guid.Empty, b.Id);
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void BuildSuperAdmin_uses_a_random_salt_so_the_same_password_hashes_differently()
    {
        var a = AdminSeeder.BuildSuperAdmin("a@b.c", "pw", Hasher());
        var b = AdminSeeder.BuildSuperAdmin("a@b.c", "pw", Hasher());

        Assert.NotEqual(a.PasswordHash, b.PasswordHash);
        Assert.NotEqual("pw", a.PasswordHash);
        Assert.False(string.IsNullOrWhiteSpace(a.PasswordHash));
    }

    [Fact]
    public void BuildSuperAdmin_hash_verifies_original_password_and_rejects_others()
    {
        var user = AdminSeeder.BuildSuperAdmin("a@b.c", "s3cr3t-pw", Hasher());

        Assert.True(Hasher().Verify(user.PasswordHash, "s3cr3t-pw"));
        Assert.False(Hasher().Verify(user.PasswordHash, "otra"));
        Assert.False(Hasher().Verify(user.PasswordHash, "S3CR3T-PW"));
        Assert.False(Hasher().Verify(user.PasswordHash, ""));
    }

    // ---- characterisation: no argument guard on email ---------------------------
    // NOT a requirement and NOT reachable from StartAsync — ShouldSeed() rejects a
    // blank Admin:BootstrapEmail before BuildSuperAdmin is ever called. Documented so a
    // future guard change is a deliberate, visible decision.

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void BuildSuperAdmin_with_blank_email_yields_empty_email_without_throwing(string configured)
    {
        var user = AdminSeeder.BuildSuperAdmin(configured, "pw", Hasher());

        Assert.Equal(string.Empty, user.Email);
        Assert.Equal(AdminRole.SuperAdmin, user.Role);
        Assert.True(user.IsActive);
        Assert.False(string.IsNullOrEmpty(user.PasswordHash));
    }

    [Fact]
    public void BuildSuperAdmin_with_null_email_throws_NullReferenceException()
    {
        // Characterisation: email.Trim() dereferences null. StartAsync never passes null
        // (ShouldSeed requires a non-blank email first).
        Assert.Throws<NullReferenceException>(
            () => AdminSeeder.BuildSuperAdmin(null!, "pw", Hasher()));
    }
}
