using System.Text.Json;
using LicensingAdmin.Auth;
using LicensingCore.Entities;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// Locks in the E2-T7 security-audit hardening: the temp-password floor and the
/// <c>Enum.IsDefined</c> check on <see cref="AdminUserService.CreateAsync"/>, the
/// lockout guards on <see cref="AdminUserService.SetActiveAsync"/>, and the
/// <see cref="AuditLogEntry.DetailsJson"/> before/after payloads.
/// </summary>
public class AdminUserServiceGuardTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static PasswordHasherService Hasher() => new(new PasswordHasher<AdminUser>());

    private sealed class FakeStore : IAdminUserStore
    {
        public readonly List<AdminUser> Rows = new();
        public (AdminUser user, AuditLogEntry audit)? Added;
        public (AdminUser user, AuditLogEntry audit)? ActiveChange;
        public int Writes;

        public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct = default) =>
            Task.FromResult(Rows.Any(u => string.Equals(u.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<AdminUser>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AdminUser>>(Rows);

        public Task<AdminUser?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Rows.FirstOrDefault(u => u.Id == id));

        public Task AddAsync(AdminUser user, AuditLogEntry audit, CancellationToken ct = default)
        {
            Added = (user, audit); Rows.Add(user); Writes++;
            return Task.CompletedTask;
        }

        public Task SetActiveAsync(AdminUser user, AuditLogEntry audit, CancellationToken ct = default)
        {
            ActiveChange = (user, audit); Writes++;
            return Task.CompletedTask;
        }
    }

    private static AdminUser Row(string email, AdminRole role, bool active = true) => new()
    {
        Id = Guid.NewGuid(), Email = email, PasswordHash = "x", Role = role, IsActive = active,
    };

    // ---- CreateAsync guards -------------------------------------------------

    [Theory]
    [InlineData("short")]
    [InlineData("01234567890")]        // 11 — one under the floor
    public async Task CreateAsync_rejects_a_temp_password_below_the_floor(string tempPassword)
    {
        Assert.True(tempPassword.Length < AdminUserService.MinTempPasswordLength);

        var svc = new AdminUserService(new FakeStore(), Hasher());
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CreateAsync("new@vendor.test", AdminRole.ReadOnlyViewer, tempPassword, "boss@vendor.test", Ct));
        Assert.Equal("tempPassword", ex.ParamName);
    }

    [Fact]
    public async Task CreateAsync_accepts_a_temp_password_at_the_floor()
    {
        var store = new FakeStore();
        var svc = new AdminUserService(store, Hasher());

        var user = await svc.CreateAsync(
            "new@vendor.test", AdminRole.ReadOnlyViewer, new string('a', AdminUserService.MinTempPasswordLength),
            "boss@vendor.test", Ct);

        Assert.Equal(1, store.Writes);
        Assert.NotNull(user);
    }

    [Fact]
    public async Task CreateAsync_rejects_an_undefined_role()
    {
        var svc = new AdminUserService(new FakeStore(), Hasher());
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CreateAsync("new@vendor.test", (AdminRole)99, "temp-horse-battery", "boss@vendor.test", Ct));
        Assert.Equal("role", ex.ParamName);
    }

    [Fact]
    public async Task CreateAsync_audit_records_the_granted_role_in_details_json()
    {
        var store = new FakeStore();
        var svc = new AdminUserService(store, Hasher());

        await svc.CreateAsync("new@vendor.test", AdminRole.SupportStaff, "temp-horse-battery", "boss@vendor.test", Ct);

        var details = store.Added!.Value.audit.DetailsJson;
        Assert.False(string.IsNullOrEmpty(details));
        Assert.Equal("SupportStaff", JsonDocument.Parse(details!).RootElement.GetProperty("role").GetString());
    }

    // ---- SetActiveAsync lockout guards -----------------------------------

    [Fact]
    public async Task SetActiveAsync_blocks_deactivating_your_own_account()
    {
        var me = Row("me@vendor.test", AdminRole.SuperAdmin);
        var other = Row("other@vendor.test", AdminRole.SuperAdmin);
        var store = new FakeStore { Rows = { me, other } };
        var svc = new AdminUserService(store, Hasher());

        await Assert.ThrowsAsync<LastSuperAdminException>(
            () => svc.SetActiveAsync(me.Id, isActive: false, "me@vendor.test", Ct));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task SetActiveAsync_blocks_deactivating_the_last_active_super_admin()
    {
        var last = Row("last@vendor.test", AdminRole.SuperAdmin);
        var inactivePeer = Row("peer@vendor.test", AdminRole.SuperAdmin, active: false);
        var staff = Row("staff@vendor.test", AdminRole.SupportStaff);
        var store = new FakeStore { Rows = { last, inactivePeer, staff } };
        var svc = new AdminUserService(store, Hasher());

        await Assert.ThrowsAsync<LastSuperAdminException>(
            () => svc.SetActiveAsync(last.Id, isActive: false, "boss@vendor.test", Ct));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task SetActiveAsync_allows_deactivating_a_super_admin_when_another_stays_active()
    {
        var one = Row("one@vendor.test", AdminRole.SuperAdmin);
        var two = Row("two@vendor.test", AdminRole.SuperAdmin);
        var store = new FakeStore { Rows = { one, two } };
        var svc = new AdminUserService(store, Hasher());

        await svc.SetActiveAsync(one.Id, isActive: false, "two@vendor.test", Ct);

        Assert.False(one.IsActive);
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public async Task SetActiveAsync_allows_deactivating_a_non_super_admin_freely()
    {
        var viewer = Row("v@vendor.test", AdminRole.ReadOnlyViewer);
        var store = new FakeStore { Rows = { viewer } };
        var svc = new AdminUserService(store, Hasher());

        await svc.SetActiveAsync(viewer.Id, isActive: false, "boss@vendor.test", Ct);

        Assert.False(viewer.IsActive);
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public async Task SetActiveAsync_audit_records_the_new_state_in_details_json()
    {
        var viewer = Row("v@vendor.test", AdminRole.ReadOnlyViewer);
        var store = new FakeStore { Rows = { viewer } };
        var svc = new AdminUserService(store, Hasher());

        await svc.SetActiveAsync(viewer.Id, isActive: false, "boss@vendor.test", Ct);

        var details = store.ActiveChange!.Value.audit.DetailsJson;
        Assert.False(string.IsNullOrEmpty(details));
        Assert.False(JsonDocument.Parse(details!).RootElement.GetProperty("isActive").GetBoolean());
    }
}
