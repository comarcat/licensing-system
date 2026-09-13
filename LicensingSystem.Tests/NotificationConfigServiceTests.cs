using LicensingAdmin.Notifications;
using LicensingCore.Entities;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// Unit tests for <see cref="NotificationConfigService"/> over fake
/// <see cref="INotificationConfigStore"/>/<see cref="IEmailSender"/> and the real
/// <see cref="EphemeralDataProtectionProvider"/> (in-memory keys — fine for a
/// process-lifetime round-trip test, never persisted). No database, no real SMTP.
/// </summary>
public class NotificationConfigServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeStore : INotificationConfigStore
    {
        public NotificationConfig? Row;
        public int SaveCalls;

        public Task<NotificationConfig?> GetAsync(CancellationToken ct = default) => Task.FromResult(Row);

        public Task SaveAsync(NotificationConfig config, CancellationToken ct = default)
        {
            Row = config;
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSender : IEmailSender
    {
        public (NotificationConfig config, string? password, string to, string subject, string body)? Sent;
        public Exception? ThrowOnSend;

        public Task SendAsync(
            NotificationConfig config, string? plainPassword, string toAddress, string subject, string body,
            CancellationToken ct = default)
        {
            if (ThrowOnSend is not null)
            {
                throw ThrowOnSend;
            }
            Sent = (config, plainPassword, toAddress, subject, body);
            return Task.CompletedTask;
        }
    }

    private static NotificationConfigService NewService(FakeStore store, RecordingSender sender) =>
        new(store, new EphemeralDataProtectionProvider(), sender);

    [Fact]
    public async Task SaveAsync_inserts_a_new_row_with_an_encrypted_password()
    {
        var store = new FakeStore();
        var svc = NewService(store, new RecordingSender());

        await svc.SaveAsync("smtp.example.com", 587, SmtpEncryption.StartTls, SmtpAuthType.Basic,
            "user@example.com", "super-secret", "noreply@example.com", Ct);

        Assert.NotNull(store.Row);
        Assert.Equal("smtp.example.com", store.Row!.SmtpHost);
        Assert.Equal(587, store.Row.SmtpPort);
        Assert.NotEmpty(store.Row.PasswordEncrypted);
        Assert.DoesNotContain(
            "super-secret", System.Text.Encoding.UTF8.GetString(store.Row.PasswordEncrypted));
    }

    [Fact]
    public async Task SaveAsync_with_blank_password_keeps_the_existing_encrypted_password()
    {
        var store = new FakeStore();
        var svc = NewService(store, new RecordingSender());
        await svc.SaveAsync("smtp.example.com", 587, SmtpEncryption.StartTls, SmtpAuthType.Basic,
            "user@example.com", "first-password", "noreply@example.com", Ct);
        var firstEncrypted = store.Row!.PasswordEncrypted;

        await svc.SaveAsync("smtp.example.com", 465, SmtpEncryption.ImplicitTls, SmtpAuthType.Basic,
            "user@example.com", plainPassword: null, "noreply@example.com", Ct);

        Assert.Equal(firstEncrypted, store.Row!.PasswordEncrypted);
        Assert.Equal(465, store.Row.SmtpPort);
        Assert.Equal(SmtpEncryption.ImplicitTls, store.Row.Encryption);
    }

    [Fact]
    public async Task SendTestEmailAsync_decrypts_the_password_and_stamps_success()
    {
        var store = new FakeStore();
        var sender = new RecordingSender();
        var svc = NewService(store, sender);
        await svc.SaveAsync("smtp.example.com", 587, SmtpEncryption.StartTls, SmtpAuthType.Basic,
            "user@example.com", "super-secret", "noreply@example.com", Ct);

        var ok = await svc.SendTestEmailAsync("dest@example.com", Ct);

        Assert.True(ok);
        Assert.NotNull(sender.Sent);
        Assert.Equal("super-secret", sender.Sent!.Value.password);
        Assert.Equal("dest@example.com", sender.Sent.Value.to);
        Assert.Equal(NotificationTestStatus.Success, store.Row!.LastTestStatus);
        Assert.NotNull(store.Row.LastTestAtUtc);
    }

    [Fact]
    public async Task SendTestEmailAsync_no_config_saved_throws()
    {
        var store = new FakeStore();
        var svc = NewService(store, new RecordingSender());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SendTestEmailAsync("dest@example.com", Ct));
    }

    [Fact]
    public async Task SendTestEmailAsync_send_failure_stamps_failed_and_rethrows()
    {
        var store = new FakeStore();
        var sender = new RecordingSender { ThrowOnSend = new InvalidOperationException("smtp down") };
        var svc = NewService(store, sender);
        await svc.SaveAsync("smtp.example.com", 587, SmtpEncryption.StartTls, SmtpAuthType.Basic,
            "user@example.com", "super-secret", "noreply@example.com", Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SendTestEmailAsync("dest@example.com", Ct));

        Assert.Equal(NotificationTestStatus.Failed, store.Row!.LastTestStatus);
        Assert.NotNull(store.Row.LastTestAtUtc);
    }
}
