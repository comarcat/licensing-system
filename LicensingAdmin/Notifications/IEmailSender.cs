using LicensingCore.Entities;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace LicensingAdmin.Notifications;

/// <summary>
/// Sends a single email through an ad-hoc SMTP connection built from a
/// <see cref="NotificationConfig"/> — no ambient/DI-registered SMTP client, since the
/// server settings are admin-configurable data, not static app config.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Connects to <paramref name="config"/>'s SMTP server, authenticates (unless
    /// <see cref="SmtpAuthType"/> and blank credentials mean anonymous relay), and sends
    /// one message with <paramref name="subject"/>/<paramref name="body"/> to
    /// <paramref name="toAddress"/> from <see cref="NotificationConfig.FromAddress"/>.
    /// </summary>
    /// <param name="config">SMTP server settings; <paramref name="plainPassword"/> carries the decrypted secret separately so callers never decrypt-then-mutate the entity.</param>
    /// <param name="plainPassword">The decrypted SMTP password, or <c>null</c>/empty for anonymous auth.</param>
    /// <exception cref="Exception">Any MailKit connection/auth/send failure propagates as-is; callers decide how to report it.</exception>
    Task SendAsync(
        NotificationConfig config, string? plainPassword, string toAddress, string subject, string body,
        CancellationToken ct = default);
}

/// <summary>MailKit-backed <see cref="IEmailSender"/>.</summary>
public sealed class MailKitEmailSender : IEmailSender
{
    /// <inheritdoc />
    public async Task SendAsync(
        NotificationConfig config, string? plainPassword, string toAddress, string subject, string body,
        CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(config.FromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        var socketOptions = config.Encryption switch
        {
            SmtpEncryption.ImplicitTls => SecureSocketOptions.SslOnConnect,
            SmtpEncryption.StartTls => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.None,
        };

        await client.ConnectAsync(config.SmtpHost, config.SmtpPort, socketOptions, ct);
        if (!string.IsNullOrEmpty(config.Username))
        {
            await client.AuthenticateAsync(config.Username, plainPassword ?? string.Empty, ct);
        }
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}
