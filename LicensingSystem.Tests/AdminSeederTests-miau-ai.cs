using LicensingAdmin.Auth;
using LicensingAdmin.Startup;
using LicensingCore.Entities;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// Deterministic unit tests for <see cref="AdminSeeder"/>'s two pure statics
/// (<see cref="AdminSeeder.ShouldSeed"/>, <see cref="AdminSeeder.BuildSuperAdmin"/>).
/// The <c>StartAsync</c> DB path is exercised by the <c>WebApplicationFactory</c> hosts
/// in the pipeline tests (which drop the seeder) and by the epic's <c># manual:</c> step
/// against real Postgres — it is not unit-tested here (needs a live <c>DbContext</c>).
/// </summary>
public class AdminSeederTests
{
    private static PasswordHasherService Hasher() => new(new PasswordHasher<AdminUser>());

    // ---- ShouldSeed --------------------------------------------------------------

    [Fact]
    public void ShouldSeed_table_empty_and_both_values_present_is_true()
    {
        Assert.True(AdminSeeder.ShouldSeed(tableEmpty: true, "a@b.c", "pw"));
    }

    [Fact]
    public void ShouldSeed_table_not_empty_is_false_even_with_both_values()
    {
        Assert.False(AdminSeeder.ShouldSeed(tableEmpty: false, "a@b.c", "pw"));
    }

    [Theory]
    [InlineData(null, "pw")]
    [InlineData("a@b.c", null)]
    [InlineData("", "pw")]
    [InlineData("a@b.c", "")]
    [InlineData("   ", "pw")]
    [InlineData("a@b.c", "   ")]
    public void ShouldSeed_missing_or_blank_email_or_password_is_false(string? email, string? password)
    {
        Assert.False(AdminSeeder.ShouldSeed(tableEmpty: true, email, password));
    }

    // ---- BuildSuperAdmin -------------------------------------------------------

    [Fact]
    public void BuildSuperAdmin_returns_an_active_super_admin_with_a_real_hash()
    {
        var user = AdminSeeder.BuildSuperAdmin("a@b.c", "pw", Hasher());

        Assert.Equal(AdminRole.SuperAdmin, user.Role);
        Assert.True(user.IsActive);
        Assert.False(string.IsNullOrEmpty(user.PasswordHash));
        Assert.NotEqual("pw", user.PasswordHash);
        Assert.True(Hasher().Verify(user.PasswordHash, "pw"));
    }

    [Theory]
    [InlineData("  Admin@B.C  ", "admin@b.c")]
    [InlineData("USER@VENDOR.TEST", "user@vendor.test")]
    [InlineData("mixed@Case.io", "mixed@case.io")]
    public void BuildSuperAdmin_normalises_the_email_trimmed_and_lowercased(string configured, string expected)
    {
        var user = AdminSeeder.BuildSuperAdmin(configured, "pw", Hasher());

        Assert.Equal(expected, user.Email);
    }

    [Fact]
    public void BuildSuperAdmin_assigns_a_non_empty_id()
    {
        // The audit row's EntityId is admin.Id.ToString(); it must be a real GUID, not
        // Guid.Empty, at the moment the AuditLogEntry is built (before SaveChanges).
        Assert.NotEqual(Guid.Empty, AdminSeeder.BuildSuperAdmin("a@b.c", "pw", Hasher()).Id);
    }
}
