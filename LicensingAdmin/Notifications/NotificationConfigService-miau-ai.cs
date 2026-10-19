using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Authentication;
using LicensingCore.Data;
using LicensingCore.Entities;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LicensingAdmin.Notifications;

/// <summary>
/// Use cases behind the Notification Settings screen: load the current SMTP config,
/// save a new one (encrypting the password at rest via ASP.NET Core Data Protection —
/// see <see cref="Purpose"/>), and send a test email against whatever is currently saved.
/// </summary>
public sealed class NotificationConfigService(
    INotificationConfigStore store,
    IDataProtectionProvider dataProtection,
    IEmailSender sender,
    ILogger<NotificationConfigService> logger,
    IDbContextFactory<AppDbContext> dbFactory)
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory = dbFactory;

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

        var sw = Stopwatch.StartNew();
        string? recipientDomain = null;
        var status = NotificationTestStatus.Success;

        try
        {
            // Extract recipient domain for diagnostic logging (never log the full address).
            var atIndex = toAddress.LastIndexOf('@');
            recipientDomain = atIndex > 0 && atIndex < toAddress.Length - 1
                ? toAddress[(atIndex + 1)..]
                : "unknown";

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
            status = NotificationTestStatus.Failed;
            sw.Stop();
            config.LastTestStatus = NotificationTestStatus.Failed;
            config.LastTestAtUtc = DateTime.UtcNow;
            await store.SaveAsync(config, ct);

            // Diagnostic log: exception type, host, port, encryption, auth type, elapsed ms,
            // recipient domain — NO password, NO full recipient address.
            logger.LogWarning(
                "Test email failed. ExceptionType={ExType}, SmtpHost={Host}, SmtpPort={Port}, " +
                "Encryption={Enc}, AuthType={Auth}, ElapsedMs={Elapsed}, RecipientDomain={Domain}",
                ex.GetType().Name, config.SmtpHost, config.SmtpPort,
                config.Encryption, config.AuthType, sw.ElapsedMilliseconds, recipientDomain);

            // Re-throw with a sanitized reason the UI can show.
            throw new InvalidOperationException(
                $"Error de conexión SMTP: {SanitizeFailureReason(ex)}", ex);
        }
        finally
        {
            await LogEmailAsync(recipientDomain ?? "unknown", status == NotificationTestStatus.Success ? "Success" : "Failed", "Test", ct);
        }
    }

    private async Task LogEmailAsync(string domain, string status, string type, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.EmailLogEntries.Add(new EmailLogEntry
        {
            Id = Guid.NewGuid(),
            RecipientDomain = domain,
            Status = status,
            EmailType = type,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Returns a short, safe failure description — no password, no stack, no full address.</summary>
    private static string SanitizeFailureReason(Exception ex) => ex switch
    {
        SmtpCommandException smtpEx => smtpEx.StatusCode switch
        {
            SmtpStatusCode.AuthenticationRequired => "Autenticación requerida",
            SmtpStatusCode.MailboxUnavailable => "Buzón de destino no disponible",
            SmtpStatusCode.TransactionFailed => "Transacción SMTP fallida",
            _ => $"SMTP {smtpEx.StatusCode}",
        },
        SocketException => "No se pudo conectar al servidor SMTP",
        System.IO.IOException ioe when ioe.InnerException is SocketException => "No se pudo conectar al servidor SMTP",
        AuthenticationException => "Autenticación fallida",
        TimeoutException => "Tiempo de espera agotado",
        _ => ex.GetType().Name,
    };
}
