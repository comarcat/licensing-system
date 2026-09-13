using LicensingCore.Data;
using LicensingCore.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

    /// <summary>
    /// Persists a new <see cref="AdminUser.PasswordHash"/> on an already-loaded
    /// <paramref name="user"/> plus <paramref name="audit"/> in one transaction.
    /// </summary>
    Task UpdatePasswordAsync(AdminUser user, string newHash, AuditLogEntry audit, CancellationToken ct = default);

    /// <summary>
    /// Persists a new, already-normalised <see cref="AdminUser.Email"/> on an
    /// already-loaded <paramref name="user"/> plus <paramref name="audit"/> in one
    /// transaction.
    /// </summary>
    /// <exception cref="AdminEmailTakenException">
    /// <paramref name="newNormalizedEmail"/> collides with another admin's email
    /// (translated from the store's unique-index violation).
    /// </exception>
    Task UpdateEmailAsync(AdminUser user, string newNormalizedEmail, AuditLogEntry audit, CancellationToken ct = default);
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
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when ((e.InnerException as PostgresException)?.SqlState == "23505")
        {
            // Lost the race with a concurrent create between EmailExistsAsync and here —
            // the unique index on admin_users.Email rejected ours. Translate to the domain
            // exception so the caller shows "email in use", not a raw Postgres message.
            throw new AdminEmailTakenException(user.Email);
        }
    }

    /// <inheritdoc />
    public async Task SetActiveAsync(AdminUser user, AuditLogEntry audit, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.AdminUsers.Attach(user).Property(u => u.IsActive).IsModified = true;
        db.AuditLogEntries.Add(audit);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task UpdatePasswordAsync(AdminUser user, string newHash, AuditLogEntry audit, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.AdminUsers.Attach(user);
        user.PasswordHash = newHash;
        db.Entry(user).Property(u => u.PasswordHash).IsModified = true;
        db.AuditLogEntries.Add(audit);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task UpdateEmailAsync(AdminUser user, string newNormalizedEmail, AuditLogEntry audit, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.AdminUsers.Attach(user);
        user.Email = newNormalizedEmail;
        db.Entry(user).Property(u => u.Email).IsModified = true;
        db.AuditLogEntries.Add(audit);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when ((e.InnerException as PostgresException)?.SqlState == "23505")
        {
            throw new AdminEmailTakenException(newNormalizedEmail);
        }
    }
}
