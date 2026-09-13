using LicensingCore.Data;
using LicensingCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace LicensingAdmin.Notifications;

/// <summary>
/// Persistence boundary for the single-row SMTP configuration. There is at most one
/// <see cref="NotificationConfig"/> row in practice (per the entity's own doc comment);
/// this store treats the table as a singleton — <see cref="GetAsync"/> returns the first
/// row or <c>null</c>, <see cref="SaveAsync"/> inserts if none exists yet, else updates
/// the existing one in place.
/// </summary>
public interface INotificationConfigStore
{
    /// <summary>The current config, or <c>null</c> if it has never been saved.</summary>
    Task<NotificationConfig?> GetAsync(CancellationToken ct = default);

    /// <summary>Inserts or updates the singleton row.</summary>
    Task SaveAsync(NotificationConfig config, CancellationToken ct = default);
}

/// <summary>
/// EF Core-backed <see cref="INotificationConfigStore"/>. Not unit tested (thin EF
/// adapter, mirrors <c>EfAdminUserStore</c> / <c>EfLicenseStore</c>).
/// </summary>
public sealed class EfNotificationConfigStore(IDbContextFactory<AppDbContext> factory) : INotificationConfigStore
{
    /// <inheritdoc />
    public async Task<NotificationConfig?> GetAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.NotificationConfigs.OrderBy(c => c.Id).FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public async Task SaveAsync(NotificationConfig config, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.NotificationConfigs.OrderBy(c => c.Id).FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            db.NotificationConfigs.Add(config);
        }
        else
        {
            existing.SmtpHost = config.SmtpHost;
            existing.SmtpPort = config.SmtpPort;
            existing.Encryption = config.Encryption;
            existing.AuthType = config.AuthType;
            existing.Username = config.Username;
            existing.PasswordEncrypted = config.PasswordEncrypted;
            existing.FromAddress = config.FromAddress;
            existing.LastTestStatus = config.LastTestStatus;
            existing.LastTestAtUtc = config.LastTestAtUtc;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }
}
