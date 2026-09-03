using LicensingCore.Data;
using LicensingCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace LicensingAdmin.Auth;

/// <summary>
/// Persistence boundary for <see cref="AdminUserService"/>: a case-insensitive email
/// probe, a list for the management screen, a by-id load for the active toggle, and two
/// atomic writes (new admin + audit; active change + audit). Abstracted from EF so the
/// service is unit-testable without a database.
/// </summary>
public interface IAdminUserStore
{
    /// <summary>
    /// <c>true</c> when an <c>admin_users</c> row already carries <paramref name="normalizedEmail"/>
    /// (the caller passes the already trimmed + lower-cased value).
    /// </summary>
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct = default);

    /// <summary>Every admin, ordered by email, for the screen.</summary>
    Task<IReadOnlyList<AdminUser>> ListAsync(CancellationToken ct = default);

    /// <summary>The admin with <paramref name="id"/>, or <c>null</c>.</summary>
    Task<AdminUser?> FindByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Persists <paramref name="user"/> and <paramref name="audit"/> together in one transaction.</summary>
    Task AddAsync(AdminUser user, AuditLogEntry audit, CancellationToken ct = default);

    /// <summary>
    /// Persists an <see cref="AdminUser.IsActive"/> change on an already-loaded
    /// <paramref name="user"/> plus <paramref name="audit"/> in one transaction.
    /// </summary>
    Task SetActiveAsync(AdminUser user, AuditLogEntry audit, CancellationToken ct = default);
}

/// <summary>
/// EF Core-backed <see cref="IAdminUserStore"/>. Each call takes a short-lived context
/// from the registered <see cref="IDbContextFactory{TContext}"/>. Not unit tested (thin
/// EF adapter, mirrors <c>EfLicenseStore</c> / <c>EfAdminUserLookup</c>).
/// </summary>
public sealed class EfAdminUserStore(IDbContextFactory<AppDbContext> factory) : IAdminUserStore
{
    /// <inheritdoc />
    public async Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // Write-side normalisation lands the stored Email in lower-case (AdminUserService),
        // but existing rows from before may not be — keep the case-insensitive compare.
        return await db.AdminUsers.AnyAsync(u => u.Email.ToLower() == normalizedEmail, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AdminUser>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.AdminUsers.OrderBy(u => u.Email).ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<AdminUser?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.AdminUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    /// <inheritdoc />
    public async Task AddAsync(AdminUser user, AuditLogEntry audit, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.AdminUsers.Add(user);
        db.AuditLogEntries.Add(audit);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task SetActiveAsync(AdminUser user, AuditLogEntry audit, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.AdminUsers.Attach(user).Property(u => u.IsActive).IsModified = true;
        db.AuditLogEntries.Add(audit);
        await db.SaveChangesAsync(ct);
    }
}
