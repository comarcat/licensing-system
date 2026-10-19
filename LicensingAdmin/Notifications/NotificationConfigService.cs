using LicensingCore.Entities;
using Microsoft.AspNetCore.DataProtection;

namespace LicensingAdmin.Notifications;

/// <summary>
/// Use cases behind the Notification Settings screen: load the current SMTP config,
/// save a new one (encrypting the password at rest via ASP.NET Core Data Protection —
/// see <see cref="Purpose"/>), and send a test email against whatever is currently saved.
/// </summary>
public sealed class NotificationConfigService(
    INotificationConfigStore store, IDataProtectionProvider dataProtection, IEmailSender sender)
{
    /// <summary>
    /// Data Protection purpose string for the SMTP password protector. Changing this
    /// string invalidates every previously-encrypted password (Data Protection ties the
    /// key to the purpose), so treat it as a stable identifier, not a comment.
    /// </summary>
    public const string Purpose = "LicensingAdmin.Notifications.SmtpPassword.v1";

    private IDataProtector Protector => dataProtection.CreateProtector(Purpose);

    /// <summary>The current config, or <c>null</c> if never configured.</summary>
    public Task<NotificationConfig?> GetAsync(CancellationToken ct = default) => store.GetAsync(ct);

    /// <summary>
    /// Encrypts <paramref name="plainPassword"/> and saves it into the singleton config
    /// row alongside the other fields. When <paramref name="plainPassword"/> is
    /// null/blank, the existing encrypted password (if any) is kept unchanged — the
    /// screen never displays a stored password back, so "leave blank to keep it" is the
    /// only way to edit other fields without re-entering the password.
    /// </summary>
    public async Task SaveAsync(
        string smtpHost, int smtpPort, SmtpEncryption encryption, SmtpAuthType authType,
        string username, string? plainPassword, string fromAddress, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(smtpHost);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromAddress);

        var existing = await store.GetAsync(ct);
        byte[] passwordEncrypted;
        if (!string.IsNullOrEmpty(plainPassword))
        {
            passwordEncrypted = Protector.Protect(System.Text.Encoding.UTF8.GetBytes(plainPassword));
        }
        else if (existing is not null)
        {
            passwordEncrypted = existing.PasswordEncrypted;
        }
        else
        {
            passwordEncrypted = [];
        }

        var config = new NotificationConfig
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            SmtpHost = smtpHost.Trim(),
            SmtpPort = smtpPort,
            Encryption = encryption,
            AuthType = authType,
            Username = username?.Trim() ?? string.Empty,
            PasswordEncrypted = passwordEncrypted,
            FromAddress = fromAddress.Trim(),
            LastTestStatus = existing?.LastTestStatus ?? NotificationTestStatus.NeverTested,
            LastTestAtUtc = existing?.LastTestAtUtc,
        };
        await store.SaveAsync(config, ct);
    }

    /// <summary>
    /// Decrypts the stored password and sends a test email to <paramref name="toAddress"/>,
    /// then stamps <see cref="NotificationConfig.LastTestStatus"/>/<see cref="NotificationConfig.LastTestAtUtc"/>
    /// with the outcome — success or failure — before returning.
    /// </summary>
    /// <exception cref="InvalidOperationException">No config has been saved yet.</exception>
    public async Task<bool> SendTestEmailAsync(string toAddress, CancellationToken ct = default)
    {
        var config = await store.GetAsync(ct)
            ?? throw new InvalidOperationException("No notification configuration has been saved yet.");

        string? plainPassword = null;
        if (config.PasswordEncrypted.Length > 0)
        {
            plainPassword = System.Text.Encoding.UTF8.GetString(Protector.Unprotect(config.PasswordEncrypted));
        }

        try
        {
            await sender.SendAsync(
                config, plainPassword, toAddress,
                "Correo de prueba — Miautrix Licensing System",
                "Este es un correo de prueba enviado desde la configuracion de notificaciones del panel de licencias.",
                ct);
            config.LastTestStatus = NotificationTestStatus.Success;
            config.LastTestAtUtc = DateTime.UtcNow;
            await store.SaveAsync(config, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            config.LastTestStatus = NotificationTestStatus.Failed;
            config.LastTestAtUtc = DateTime.UtcNow;
            await store.SaveAsync(config, ct);
            throw;
        }
    }
}
