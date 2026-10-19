using LicensingAdmin.Auth;
using LicensingCore.Entities;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// E2-T7 extra coverage: characterisation + edge cases for <see cref="AdminUserService"/>
/// on top of <see cref="AdminUserServiceTests"/>. Same recipe — a hand-rolled
/// <see cref="IAdminUserStore"/> fake that captures every write, no database.
///
/// What this file pins that the base file did not:
///  * every <see cref="AdminRole"/> (incl. <see cref="AdminRole.SuperAdmin"/>) round-trips
///    through <c>CreateAsync</c> unchanged and unrestricted;
///  * the "Created" audit row and the new <see cref="AdminUser"/> reach the store in the
///    SAME <c>AddAsync</c> call (one transaction);
///  * <c>SetActiveAsync</c> writes "Updated" for both toggle directions, stamps
///    <c>EntityId == id.ToString()</c>, and hands back the very instance it loaded;
///  * <c>CreateAsync</c> mints two distinct non-empty GUIDs (user vs audit) and a fresh
///    <c>Id</c> per call, and leaves <c>CreatedAtUtc</c> initialised;
///  * <see cref="AdminEmailTakenException.NormalizedEmail"/> / message carry the collided
///    normalised email (oracle: current behaviour, documented not required);
///  * <c>NormalizeEmail(null)</c> => "", internal whitespace survives, Unicode is
///    invariant-lower-cased.
/// </summary>
public class AdminUserServiceEdgeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PasswordHasherService Hasher() => new(new PasswordHasher<AdminUser>());

    /// <summary>
    /// Fake store that records each write separately (so a test can tell an insert from an
    /// active-toggle) and keeps the exact <c>(user, audit)</c> tuple handed to each call.
    /// </summary>
    private sealed class RecordingStore : IAdminUserStore
    {
        public readonly List<string> ExistingEmails = new();
        public readonly List<AdminUser> Rows = new();

        public (AdminUser user, AuditLogEntry audit)? Added;
        public (AdminUser user, AuditLogEntry audit)? ActiveChange;
        public int AddCalls;
        public int SetActiveCalls;
        public int Writes => AddCalls + SetActiveCalls;

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
            AddCalls++;
            return Task.CompletedTask;
        }

        public Task SetActiveAsync(AdminUser user, AuditLogEntry audit, CancellationToken ct = default)
        {
            ActiveChange = (user, audit);
            SetActiveCalls++;
            return Task.CompletedTask;
        }

        public Task UpdatePasswordAsync(AdminUser user, string newHash, AuditLogEntry audit, CancellationToken ct = default)
        {
            user.PasswordHash = newHash;
            return Task.CompletedTask;
        }

        public Task UpdateEmailAsync(AdminUser user, string newNormalizedEmail, AuditLogEntry audit, CancellationToken ct = default)
        {
            if (ExistingEmails.Any(e => string.Equals(e, newNormalizedEmail, StringComparison.OrdinalIgnoreCase))
                && !string.Equals(user.Email, newNormalizedEmail, StringComparison.OrdinalIgnoreCase))
            {
                throw new AdminEmailTakenException(newNormalizedEmail);
            }
            user.Email = newNormalizedEmail;
            return Task.CompletedTask;
        }
    }

    private static AdminUser Row(string email, bool active) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        PasswordHash = "x",
        Role = AdminRole.ReadOnlyViewer,
        IsActive = active,
    };

    // ---- CreateAsync: every role round-trips, SuperAdmin included ---------------

    [Theory]
    [InlineData(AdminRole.SuperAdmin)]
    [InlineData(AdminRole.SupportStaff)]
    [InlineData(AdminRole.ReadOnlyViewer)]
    public async Task CreateAsync_preserves_the_requested_role_exactly(AdminRole role)
    {
        var store = new RecordingStore();
        var svc = new AdminUserService(store, Hasher());

        var user = await svc.CreateAsync("new@vendor.test", role, "temp-horse-battery", "boss@vendor.test", Ct);

        Assert.Equal(role, user.Role);
        Assert.Equal(role, store.Added!.Value.user.Role);
    }

    [Fact]
    public async Task CreateAsync_does_not_restrict_creating_a_SuperAdmin()
    {
        // Characterisation: there is no "you cannot mint a peer SuperAdmin" guard today.
        // If the security audit wants one, that is a product change, not a test fix.
        var store = new RecordingStore();
        var svc = new AdminUserService(store, Hasher());

        var user = await svc.CreateAsync("peer@vendor.test", AdminRole.SuperAdmin, "temp-horse-battery", "boss@vendor.test", Ct);

        Assert.Equal(AdminRole.SuperAdmin, user.Role);
        Assert.True(user.IsActive);
        Assert.Equal(1, store.AddCalls);
    }

    // ---- CreateAsync: user + audit land in ONE AddAsync call ------------------

    [Fact]
    public async Task CreateAsync_writes_the_user_and_its_audit_row_in_the_same_AddAsync_call()
    {
        var store = new RecordingStore();
        var svc = new AdminUserService(store, Hasher());

        var user = await svc.CreateAsync("new@vendor.test", AdminRole.SupportStaff, "temp-horse-battery", "boss@vendor.test", Ct);

        Assert.Equal(1, store.AddCalls);
        Assert.Equal(0, store.SetActiveCalls);
        Assert.NotNull(store.Added);
        // The fake captures both args of the single call; same tuple => same transaction.
        Assert.Same(user, store.Added!.Value.user);
        var audit = store.Added!.Value.audit;
        Assert.Equal("Created", audit.Action);
        Assert.Equal("AdminUser", audit.EntityType);
        Assert.Equal(user.Id.ToString(), audit.EntityId);
        Assert.Equal("boss@vendor.test", audit.Actor);
    }

    // ---- CreateAsync: GUID hygiene ------------------------------------------

    [Fact]
    public async Task CreateAsync_mints_two_distinct_non_empty_guids_for_user_and_audit()
    {
        var store = new RecordingStore();
        var svc = new AdminUserService(store, Hasher());

        var user = await svc.CreateAsync("new@vendor.test", AdminRole.ReadOnlyViewer, "temp-horse-battery", "boss@vendor.test", Ct);
        var audit = store.Added!.Value.audit;

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.NotEqual(Guid.Empty, audit.Id);
        Assert.NotEqual(user.Id, audit.Id);
    }

    [Fact]
    public async Task CreateAsync_twice_yields_distinct_user_ids()
    {
        var store = new RecordingStore();
        var svc = new AdminUserService(store, Hasher());

        var a = await svc.CreateAsync("a@vendor.test", AdminRole.ReadOnlyViewer, "temp-horse-battery", "boss@vendor.test", Ct);
        var b = await svc.CreateAsync("b@vendor.test", AdminRole.ReadOnlyViewer, "temp-horse-battery", "boss@vendor.test", Ct);

        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(2, store.AddCalls);
    }

    [Fact]
    public async Task CreateAsync_leaves_CreatedAtUtc_initialised_to_roughly_now()
    {
        var store = new RecordingStore();
        var svc = new AdminUserService(store, Hasher());

        var before = DateTime.UtcNow.AddMinutes(-1);
        var user = await svc.CreateAsync("new@vendor.test", AdminRole.ReadOnlyViewer, "temp-horse-battery", "boss@vendor.test", Ct);
        var after = DateTime.UtcNow.AddMinutes(1);

        // The service never touches CreatedAtUtc; this pins the entity default (DateTime.UtcNow).
        Assert.NotEqual(default, user.CreatedAtUtc);
        Assert.InRange(user.CreatedAtUtc, before, after);
        Assert.Null(user.LastLoginAtUtc);
    }

    // ---- AdminEmailTakenException characterisation --------------------------

    [Fact]
    public async Task CreateAsync_duplicate_email_exception_carries_the_normalised_email()
    {
        var store = new RecordingStore { ExistingEmails = { "taken@vendor.test" } };
        var svc = new AdminUserService(store, Hasher());

        var ex = await Assert.ThrowsAsync<AdminEmailTakenException>(
            () => svc.CreateAsync("  TAKEN@Vendor.Test ", AdminRole.ReadOnlyViewer, "temp-horse-battery", "boss@vendor.test", Ct));

        // The collided email is on the typed property; it is deliberately kept OUT of the
        // Message so a broad log sink (Users.razor catch) never records an admin address.
        Assert.Equal("taken@vendor.test", ex.NormalizedEmail);
        Assert.DoesNotContain("taken@vendor.test", ex.Message);
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
        Assert.Equal(0, store.Writes);
    }

    // ---- SetActiveAsync: both directions write "Updated" -------------------

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SetActiveAsync_writes_one_updated_row_for_either_toggle_direction(bool initial, bool target)
    {
        var row = Row("who@vendor.test", active: initial);
        var store = new RecordingStore { Rows = { row } };
        var svc = new AdminUserService(store, Hasher());

        var returned = await svc.SetActiveAsync(row.Id, target, "boss@vendor.test", Ct);

        Assert.Equal(1, store.SetActiveCalls);
        Assert.Equal(0, store.AddCalls);
        // The returned instance is the one FindByIdAsync produced, mutated in place.
        Assert.Same(row, returned);
        Assert.Equal(target, returned.IsActive);

        var audit = store.ActiveChange!.Value.audit;
        Assert.Equal("Updated", audit.Action);
        Assert.Equal("AdminUser", audit.EntityType);
        Assert.Equal(row.Id.ToString(), audit.EntityId);
        Assert.Equal("boss@vendor.test", audit.Actor);
        Assert.NotEqual(Guid.Empty, audit.Id);
        Assert.Same(row, store.ActiveChange!.Value.user);
    }

    [Fact]
    public async Task SetActiveAsync_returns_the_same_instance_on_a_noop_too()
    {
        var row = Row("who@vendor.test", active: true);
        var store = new RecordingStore { Rows = { row } };
        var svc = new AdminUserService(store, Hasher());

        var returned = await svc.SetActiveAsync(row.Id, isActive: true, "boss@vendor.test", Ct);

        Assert.Same(row, returned);
        Assert.True(returned.IsActive);
        Assert.Equal(0, store.Writes);
    }

    // ---- NormalizeEmail edge cases ---------------------------------------

    [Fact]
    public void NormalizeEmail_null_becomes_empty_string()
    {
        // (email ?? string.Empty).Trim().ToLowerInvariant() => "" — no NullReferenceException.
        Assert.Equal(string.Empty, AdminUserService.NormalizeEmail(null!));
    }

    [Theory]
    [InlineData("  outer@spaces.test  ", "outer@spaces.test")]
    [InlineData("\t tab@ws.test \r\n", "tab@ws.test")]
    [InlineData("a b@internal.test", "a b@internal.test")]  // internal whitespace is NOT stripped
    [InlineData("  MID space@Vendor.TEST  ", "mid space@vendor.test")]
    public void NormalizeEmail_only_trims_outer_whitespace(string input, string expected) =>
        Assert.Equal(expected, AdminUserService.NormalizeEmail(input));

    [Theory]
    [InlineData("ÜSER@Vendor.TEST", "üser@vendor.test")]
    [InlineData("AÇÃO@Example.COM", "ação@example.com")]
    [InlineData("GROSS.STRASSE@ẞ.TEST", "gross.strasse@ß.test")]
    public void NormalizeEmail_invariant_lowercases_unicode(string input, string expected) =>
        Assert.Equal(expected, AdminUserService.NormalizeEmail(input));
}
