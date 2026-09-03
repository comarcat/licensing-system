using LicensingAdmin.Auth;
using LicensingCore.Entities;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// Deterministic unit tests for <see cref="AdminUserService"/> over a fake
/// <see cref="IAdminUserStore"/> that records every call. No database.
/// </summary>
public class AdminUserServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PasswordHasherService Hasher() => new(new PasswordHasher<AdminUser>());

    private sealed class FakeStore : IAdminUserStore
    {
        public readonly List<string> ExistingEmails = new();
        public readonly List<AdminUser> Rows = new();

        public (AdminUser user, AuditLogEntry audit)? Added;
        public (AdminUser user, AuditLogEntry audit)? ActiveChange;
        public int Writes;

        public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct = default) =>
            Task.FromResult(ExistingEmails.Any(e => string.Equals(e, normalizedEmail, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<AdminUser>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AdminUser>>(Rows);

        public Task<AdminUser?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Rows.FirstOrDefault(u => u.Id == id));

        public Task AddAsync(AdminUser user, AuditLogEntry audit, CancellationToken ct = default)
        {
            Added = (user, audit);
            Rows.Add(user);
            Writes++;
            return Task.CompletedTask;
        }

        public Task SetActiveAsync(AdminUser user, AuditLogEntry audit, CancellationToken ct = default)
        {
            ActiveChange = (user, audit);
            Writes++;
            return Task.CompletedTask;
        }
    }

    private static AdminUser Row(string email, bool active = true) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        PasswordHash = "x",
        Role = AdminRole.ReadOnlyViewer,
        IsActive = active,
    };

    // ---- CreateAsync ----------------------------------------------------------

    [Fact]
    public async Task CreateAsync_hashes_the_temp_password_and_keeps_the_role()
    {
        var store = new FakeStore();
        var svc = new AdminUserService(store, Hasher());

        var user = await svc.CreateAsync("new@vendor.test", AdminRole.SupportStaff, "temp-horse-battery", "boss@vendor.test", Ct);

        Assert.Equal(AdminRole.SupportStaff, user.Role);
        Assert.True(user.IsActive);
        Assert.NotEqual("temp-horse-battery", user.PasswordHash);
        Assert.False(string.IsNullOrEmpty(user.PasswordHash));
        Assert.True(Hasher().Verify(user.PasswordHash, "temp-horse-battery"));
    }

    [Fact]
    public async Task CreateAsync_normalises_the_email_on_write()
    {
        var store = new FakeStore();
        var svc = new AdminUserService(store, Hasher());

        var user = await svc.CreateAsync("  New.Admin@Vendor.TEST  ", AdminRole.ReadOnlyViewer, "temp-horse-battery", "boss@vendor.test", Ct);

        Assert.Equal("new.admin@vendor.test", user.Email);
    }

    [Fact]
    public async Task CreateAsync_writes_one_created_adminuser_audit_row()
    {
        var store = new FakeStore();
        var svc = new AdminUserService(store, Hasher());

        var user = await svc.CreateAsync("new@vendor.test", AdminRole.ReadOnlyViewer, "temp-horse-battery", "boss@vendor.test", Ct);

        Assert.Equal(1, store.Writes);
        Assert.NotNull(store.Added);
        var audit = store.Added!.Value.audit;
        Assert.Equal("Created", audit.Action);
        Assert.Equal("AdminUser", audit.EntityType);
        Assert.Equal(user.Id.ToString(), audit.EntityId);
        Assert.Equal("boss@vendor.test", audit.Actor);
        Assert.NotEqual(Guid.Empty, audit.Id);
    }

    [Theory]
    [InlineData("taken@vendor.test")]
    [InlineData("TAKEN@VENDOR.TEST")]
    [InlineData("  Taken@Vendor.Test ")]
    public async Task CreateAsync_rejects_a_case_insensitively_existing_email_without_a_store_write(string attempt)
    {
        var store = new FakeStore { ExistingEmails = { "taken@vendor.test" } };
        var svc = new AdminUserService(store, Hasher());

        await Assert.ThrowsAsync<AdminEmailTakenException>(
            () => svc.CreateAsync(attempt, AdminRole.ReadOnlyViewer, "temp-horse-battery", "boss@vendor.test", Ct));

        Assert.Equal(0, store.Writes);
        Assert.Null(store.Added);
    }

    [Theory]
    [InlineData("", "temp-horse-battery", "boss@vendor.test")]
    [InlineData("   ", "temp-horse-battery", "boss@vendor.test")]
    [InlineData("new@vendor.test", "", "boss@vendor.test")]
    [InlineData("new@vendor.test", "   ", "boss@vendor.test")]
    [InlineData("new@vendor.test", "temp-horse-battery", "")]
    [InlineData("new@vendor.test", "temp-horse-battery", "   ")]
    public async Task CreateAsync_rejects_blank_arguments(string email, string password, string actor)
    {
        var store = new FakeStore();
        var svc = new AdminUserService(store, Hasher());

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CreateAsync(email, AdminRole.ReadOnlyViewer, password, actor, Ct));

        Assert.Equal(0, store.Writes);
    }

    // ---- SetActiveAsync -----------------------------------------------------

    [Fact]
    public async Task SetActiveAsync_real_change_writes_one_updated_adminuser_audit_row()
    {
        var row = Row("who@vendor.test", active: true);
        var store = new FakeStore { Rows = { row } };
        var svc = new AdminUserService(store, Hasher());

        await svc.SetActiveAsync(row.Id, isActive: false, "boss@vendor.test", Ct);

        Assert.False(row.IsActive);
        Assert.Equal(1, store.Writes);
        var audit = store.ActiveChange!.Value.audit;
        Assert.Equal("Updated", audit.Action);
        Assert.Equal("AdminUser", audit.EntityType);
        Assert.Equal(row.Id.ToString(), audit.EntityId);
        Assert.Equal("boss@vendor.test", audit.Actor);
    }

    [Fact]
    public async Task SetActiveAsync_noop_when_value_already_matches_writes_nothing()
    {
        var row = Row("who@vendor.test", active: true);
        var store = new FakeStore { Rows = { row } };
        var svc = new AdminUserService(store, Hasher());

        await svc.SetActiveAsync(row.Id, isActive: true, "boss@vendor.test", Ct);

        Assert.Equal(0, store.Writes);
        Assert.Null(store.ActiveChange);
    }

    [Fact]
    public async Task SetActiveAsync_unknown_id_throws_and_writes_nothing()
    {
        var store = new FakeStore();
        var svc = new AdminUserService(store, Hasher());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SetActiveAsync(Guid.NewGuid(), isActive: false, "boss@vendor.test", Ct));

        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task SetActiveAsync_rejects_a_blank_actor()
    {
        var row = Row("who@vendor.test");
        var store = new FakeStore { Rows = { row } };
        var svc = new AdminUserService(store, Hasher());

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.SetActiveAsync(row.Id, isActive: false, "  ", Ct));

        Assert.Equal(0, store.Writes);
    }

    // ---- NormalizeEmail ---------------------------------------------------

    [Theory]
    [InlineData("  A@B.C ", "a@b.c")]
    [InlineData("MixedCase@Vendor.IO", "mixedcase@vendor.io")]
    [InlineData("already@lower.test", "already@lower.test")]
    public void NormalizeEmail_trims_and_lowercases(string input, string expected) =>
        Assert.Equal(expected, AdminUserService.NormalizeEmail(input));
}
