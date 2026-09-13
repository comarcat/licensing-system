using LicensingCore.Data;
using LicensingCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace LicensingAdmin.Licensing;

/// <summary>
/// Manual lifecycle actions on an already-issued license (E4 item 9): revoke (a real
/// status change — the license stops validating for the client DLL) and archive (a pure
/// soft-delete — hidden from the default list, status untouched, nothing physically
/// removed). Both write an <see cref="AuditLogEntry"/>, matching every other mutation in
/// the app.
/// </summary>
public sealed class LicenseLifecycleService(IDbContextFactory<AppDbContext> factory)
{
    /// <exception cref="ArgumentException"><paramref name="actor"/> is null/blank.</exception>
    /// <exception cref="InvalidOperationException">No license with <paramref name="licenseId"/>.</exception>
    public async Task RevokeAsync(Guid licenseId, string? reason, string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        await using var db = await factory.CreateDbContextAsync(ct);
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == licenseId, ct)
            ?? throw new InvalidOperationException($"No license with id '{licenseId}'.");

        if (license.Status == LicenseStatus.Revoked)
        {
            return;
        }

        license.Status = LicenseStatus.Revoked;
        license.RevokedAtUtc = DateTime.UtcNow;
        license.RevokedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            Actor = actor,
            EntityType = "License",
            EntityId = license.Id.ToString(),
            Action = "Revoked",
        });
        await db.SaveChangesAsync(ct);
    }

    /// <exception cref="ArgumentException"><paramref name="actor"/> is null/blank.</exception>
    /// <exception cref="InvalidOperationException">No license with <paramref name="licenseId"/>.</exception>
    public async Task SetArchivedAsync(Guid licenseId, bool archived, string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        await using var db = await factory.CreateDbContextAsync(ct);
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == licenseId, ct)
            ?? throw new InvalidOperationException($"No license with id '{licenseId}'.");

        if (license.IsArchived == archived)
        {
            return;
        }

        license.IsArchived = archived;
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            Actor = actor,
            EntityType = "License",
            EntityId = license.Id.ToString(),
            Action = archived ? "Archived" : "Unarchived",
        });
        await db.SaveChangesAsync(ct);
    }
}
