using LicensingAdmin.Licensing;
using LicensingCore.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// Guard-clause coverage for <see cref="LicenseLifecycleService"/> without a real
/// database: <see cref="ThrowingDbContextFactory"/> fails the test if the service ever
/// asks for a <see cref="AppDbContext"/>, so these tests prove the actor validation
/// runs — and rejects — before any DB access is attempted.
/// </summary>
public class LicenseLifecycleServiceGuardTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class ThrowingDbContextFactory : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() =>
            throw new InvalidOperationException("Should not reach the database for this guard.");

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Should not reach the database for this guard.");
    }

    [Fact]
    public async Task RevokeAsync_rejects_a_blank_actor_without_touching_the_database()
    {
        var svc = new LicenseLifecycleService(new ThrowingDbContextFactory());

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.RevokeAsync(Guid.NewGuid(), "reason", "  ", Ct));
    }

    [Fact]
    public async Task RevokeAsync_rejects_a_null_actor_without_touching_the_database()
    {
        var svc = new LicenseLifecycleService(new ThrowingDbContextFactory());

        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException (a
        // subtype) specifically for null — xUnit's ThrowsAsync<T> requires an exact
        // type match, not "is-a", so this needs the more specific type.
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => svc.RevokeAsync(Guid.NewGuid(), "reason", null!, Ct));
    }

    [Fact]
    public async Task SetArchivedAsync_rejects_a_blank_actor_without_touching_the_database()
    {
        var svc = new LicenseLifecycleService(new ThrowingDbContextFactory());

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.SetArchivedAsync(Guid.NewGuid(), true, "", Ct));
    }
}
