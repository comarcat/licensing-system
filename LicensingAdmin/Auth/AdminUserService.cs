using LicensingCore.Entities;

namespace LicensingAdmin.Auth;

/// <summary>
/// The admin-user management use cases behind <c>Pages/Admin/Users.razor</c>: create a
/// new admin (email normalised on write, temp password hashed, never stored plain) and
/// flip an existing admin's <see cref="AdminUser.IsActive"/>. Every state change writes
/// an <see cref="AuditLogEntry"/>. Orchestration only — persistence is <see cref="IAdminUserStore"/>.
/// </summary>
public sealed class AdminUserService(IAdminUserStore store, PasswordHasherService hasher)
{
    /// <summary>Trimmed + invariant-lower-cased, matching <c>AdminSeeder</c> and <c>EfAdminUserLookup</c>.</summary>
    public static string NormalizeEmail(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Creates an active admin for <paramref name="email"/> with <paramref name="role"/> and a
    /// hash of <paramref name="tempPassword"/>. Rejects (with no store write) an email that
    /// already exists case-insensitively. Writes a "Created" / "AdminUser" audit row for
    /// <paramref name="actor"/> in the same transaction as the insert.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="email"/>, <paramref name="tempPassword"/> or <paramref name="actor"/> is null/blank.</exception>
    /// <exception cref="AdminEmailTakenException">An admin with that email already exists.</exception>
    public async Task<AdminUser> CreateAsync(
        string email, AdminRole role, string tempPassword, string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(tempPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var normalized = NormalizeEmail(email);
        if (await store.EmailExistsAsync(normalized, ct))
        {
            throw new AdminEmailTakenException(normalized);
        }

        var user = new AdminUser
        {
            Id = Guid.NewGuid(),
            Email = normalized,
            PasswordHash = hasher.Hash(tempPassword),
            Role = role,
            IsActive = true,
        };

        await store.AddAsync(user, Audit(actor, user.Id, "Created"), ct);
        return user;
    }

    /// <summary>
    /// Sets <paramref name="id"/>'s <see cref="AdminUser.IsActive"/> to <paramref name="isActive"/>.
    /// A no-op (value already matches) writes nothing. A real change writes an
    /// "Updated" / "AdminUser" audit row for <paramref name="actor"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="actor"/> is null/blank.</exception>
    /// <exception cref="InvalidOperationException">No admin with <paramref name="id"/>.</exception>
    public async Task<AdminUser> SetActiveAsync(
        Guid id, bool isActive, string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var user = await store.FindByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"No admin user with id '{id}'.");

        if (user.IsActive == isActive)
        {
            return user;
        }

        user.IsActive = isActive;
        await store.SetActiveAsync(user, Audit(actor, user.Id, "Updated"), ct);
        return user;
    }

    /// <summary>Every admin, for the screen.</summary>
    public Task<IReadOnlyList<AdminUser>> ListAsync(CancellationToken ct = default) => store.ListAsync(ct);

    private static AuditLogEntry Audit(string actor, Guid adminId, string action) => new()
    {
        Id = Guid.NewGuid(),
        Actor = actor,
        EntityType = "AdminUser",
        EntityId = adminId.ToString(),
        Action = action,
    };
}

/// <summary>Thrown by <see cref="AdminUserService.CreateAsync"/> when the email is already in use.</summary>
public sealed class AdminEmailTakenException(string normalizedEmail)
    : InvalidOperationException($"An admin with email '{normalizedEmail}' already exists.")
{
    /// <summary>The normalised email that collided.</summary>
    public string NormalizedEmail { get; } = normalizedEmail;
}
