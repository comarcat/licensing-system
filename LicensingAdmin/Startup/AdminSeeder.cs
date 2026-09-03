using LicensingAdmin.Auth;
using LicensingCore.Data;
using LicensingCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace LicensingAdmin.Startup;

/// <summary>
/// Seeds the first <see cref="AdminRole.SuperAdmin"/> at host startup when
/// <c>admin_users</c> is empty and both <c>Admin:BootstrapEmail</c> and
/// <c>Admin:BootstrapPassword</c> are configured. Once a real admin exists — or if the
/// bootstrap config is absent — it does nothing, so it is safe to leave registered.
/// </summary>
public sealed class AdminSeeder(
    IDbContextFactory<AppDbContext> dbFactory,
    PasswordHasherService hasher,
    IConfiguration config,
    ILogger<AdminSeeder> logger) : IHostedService
{
    /// <summary>
    /// <c>true</c> only when the table is empty <b>and</b> both bootstrap values are
    /// present and non-blank. Pure and static so it is unit-testable without a host.
    /// </summary>
    public static bool ShouldSeed(bool tableEmpty, string? email, string? password) =>
        tableEmpty
        && !string.IsNullOrWhiteSpace(email)
        && !string.IsNullOrWhiteSpace(password);

    /// <summary>
    /// A <see cref="AdminRole.SuperAdmin"/> row for <paramref name="email"/>, with the
    /// email trimmed and lower-cased. <c>admin_users.Email</c>'s unique index is
    /// case-sensitive and <c>EfAdminUserLookup</c> already queries in lower-case, so
    /// normalising on the write side lets the bootstrap admin sign in whatever the case
    /// of <c>Admin:BootstrapEmail</c>.
    /// </summary>
    public static AdminUser BuildSuperAdmin(string email, string password, PasswordHasherService hasher) =>
        new()
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = hasher.Hash(password),
            Role = AdminRole.SuperAdmin,
            IsActive = true,
        };

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var email = config["Admin:BootstrapEmail"];
        var password = config["Admin:BootstrapPassword"];

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!ShouldSeed(!await db.AdminUsers.AnyAsync(cancellationToken), email, password))
        {
            return;
        }

        // email/password are non-blank here (ShouldSeed). BuildSuperAdmin applies the
        // same Trim()/ToLowerInvariant() to the configured value that it does anywhere.
        var admin = BuildSuperAdmin(email!, password!, hasher);
        db.AdminUsers.Add(admin);
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            Actor = "system",
            EntityType = "AdminUser",
            EntityId = admin.Id.ToString(),
            Action = "Created",
        });
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded bootstrap SuperAdmin {Email}.", admin.Email);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
