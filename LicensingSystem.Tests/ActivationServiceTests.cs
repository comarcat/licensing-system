using System.Security.Cryptography;
using LicensingApi.Dtos;
using LicensingApi.Services;
using LicensingCore.Data;
using LicensingCore.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// Coverage for <see cref="ActivationService"/> (/api/activate and /api/checkin) — a real
/// EF Core (Sqlite in-memory) context, the real <see cref="HardwareMatchService"/> and a
/// real <see cref="LicenseFileService"/>. This service had zero test coverage before
/// 2026-09-13 (see <see cref="LicenseFileServiceTests"/> for the production incident that
/// motivated closing that gap); this file does the same for the activation/checkin flow
/// itself, including two review-enforcement gaps found and fixed while writing it:
/// a Rejected/Revoked activation could still renew via checkin, and a PendingReview
/// activation past its 15-day <see cref="Activation.ReviewDeadlineUtc"/> was never locked
/// (the <c>ResultCode.Locked</c> "grace period ended" outcome existed but nothing produced it).
/// </summary>
public sealed class ActivationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly ActivationService _service;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ActivationServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        var rsa = RSA.Create(2048);
        _service = new ActivationService(_db, new HardwareMatchService(), new LicenseFileService(rsa));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static HardwareInfo Hw(string suffix = "1") => new()
    {
        CpuId = $"CPU-{suffix}",
        MotherboardSerial = $"MB-{suffix}",
        TpmId = $"TPM-{suffix}",
        MacAddressPrimary = $"MAC-{suffix}",
    };

    private async Task<License> SeedLicenseAsync(
        LicenseStatus status = LicenseStatus.Active,
        int maxActivations = 5,
        DateTime? subscriptionExpiryUtc = null)
    {
        var product = new SoftwareProduct { Id = Guid.NewGuid(), Name = "Widget", Vendor = "Acme" };
        var license = new License
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Product = product,
            LicenseKey = "ABCD-EFGH1-IJKL-MNOP-QRST-UVWX-YZ",
            MaxActivations = maxActivations,
            Status = status,
            SubscriptionExpiryUtc = subscriptionExpiryUtc,
            Signature = new byte[] { 1, 2, 3 },
        };
        _db.SoftwareProducts.Add(product);
        _db.Licenses.Add(license);
        await _db.SaveChangesAsync(Ct);
        return license;
    }

    private async Task<Activation> SeedActivationAsync(
        License license,
        HardwareInfo hw,
        ActivationStatus status = ActivationStatus.PendingReview,
        DateTime? reviewDeadlineUtc = null)
    {
        var activation = new Activation
        {
            Id = Guid.NewGuid(),
            LicenseId = license.Id,
            InstallGuid = Guid.NewGuid(),
            CpuId = hw.CpuId,
            MotherboardSerial = hw.MotherboardSerial,
            TpmId = hw.TpmId,
            MacAddressPrimary = hw.MacAddressPrimary,
            Status = status,
            ReviewDeadlineUtc = reviewDeadlineUtc
                ?? (status == ActivationStatus.PendingReview ? DateTime.UtcNow.AddDays(15) : null),
        };
        _db.Activations.Add(activation);
        await _db.SaveChangesAsync(Ct);
        return activation;
    }

    // ---------------------------------------------------------------------
    // ActivateAsync
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Activate_InvalidKeyFormat_Fails()
    {
        var result = await _service.ActivateAsync(new ActivateRequest
        {
            LicenseKey = "not-a-key",
            InstallGuid = Guid.NewGuid(),
            Hardware = Hw(),
        }, Ct);

        Assert.False(result.Success);
        Assert.Equal(ResultCode.InvalidKeyFormat, result.Code);
    }

    [Fact]
    public async Task Activate_UnknownKey_Fails()
    {
        var result = await _service.ActivateAsync(new ActivateRequest
        {
            LicenseKey = "ABCD-EFGH1-IJKL-MNOP-QRST-UVWX-YZ",
            InstallGuid = Guid.NewGuid(),
            Hardware = Hw(),
        }, Ct);

        Assert.False(result.Success);
        Assert.Equal(ResultCode.LicenseNotFound, result.Code);
    }

    [Fact]
    public async Task Activate_RevokedLicense_Fails()
    {
        var license = await SeedLicenseAsync(LicenseStatus.Revoked);

        var result = await _service.ActivateAsync(new ActivateRequest
        {
            LicenseKey = license.LicenseKey,
            InstallGuid = Guid.NewGuid(),
            Hardware = Hw(),
        }, Ct);

        Assert.False(result.Success);
        Assert.Equal(ResultCode.LicenseRevoked, result.Code);
    }

    [Fact]
    public async Task Activate_ExpiredLicense_Fails()
    {
        var license = await SeedLicenseAsync(LicenseStatus.Expired);

        var result = await _service.ActivateAsync(new ActivateRequest
        {
            LicenseKey = license.LicenseKey,
            InstallGuid = Guid.NewGuid(),
            Hardware = Hw(),
        }, Ct);

        Assert.False(result.Success);
        Assert.Equal(ResultCode.LicenseExpired, result.Code);
    }

    [Fact]
    public async Task Activate_NewInstall_CreatesPendingReviewActivationWithSignedFile()
    {
        var license = await SeedLicenseAsync();

        var result = await _service.ActivateAsync(new ActivateRequest
        {
            LicenseKey = license.LicenseKey,
            InstallGuid = Guid.NewGuid(),
            Hardware = Hw(),
        }, Ct);

        Assert.True(result.Success);
        Assert.Equal(ResultCode.PendingReview, result.Code);
        Assert.Equal("pending_review", result.Data!.Status);
        Assert.False(string.IsNullOrEmpty(result.Data.LicenseFileBase64));
        Assert.Equal(1, await _db.Activations.CountAsync(Ct));
    }

    [Fact]
    public async Task Activate_SameHardwareReactivatesApprovedActivation_StaysApproved()
    {
        var license = await SeedLicenseAsync();
        var hw = Hw();
        var existing = await SeedActivationAsync(license, hw, ActivationStatus.Approved);
        var newGuid = Guid.NewGuid();

        var result = await _service.ActivateAsync(new ActivateRequest
        {
            LicenseKey = license.LicenseKey,
            InstallGuid = newGuid,
            Hardware = hw,
        }, Ct);

        Assert.True(result.Success);
        Assert.Equal(ResultCode.Activated, result.Code);
        Assert.Equal("approved", result.Data!.Status);
        Assert.Equal(existing.Id, result.Data.ActivationId);
        Assert.Equal(1, await _db.Activations.CountAsync(Ct));
        var reloaded = await _db.Activations.FirstAsync(a => a.Id == existing.Id, Ct);
        Assert.Equal(newGuid, reloaded.InstallGuid);
    }

    [Fact]
    public async Task Activate_SameHardwareOnPendingReview_DoesNotAutoApprove()
    {
        var license = await SeedLicenseAsync();
        var hw = Hw();
        await SeedActivationAsync(license, hw, ActivationStatus.PendingReview);

        var result = await _service.ActivateAsync(new ActivateRequest
        {
            LicenseKey = license.LicenseKey,
            InstallGuid = Guid.NewGuid(),
            Hardware = hw,
        }, Ct);

        Assert.True(result.Success);
        Assert.Equal(ResultCode.PendingReview, result.Code);
        Assert.Equal("pending_review", result.Data!.Status);
    }

    [Fact]
    public async Task Activate_RejectedActivationSameHardware_CreatesFreshPendingReview()
    {
        var license = await SeedLicenseAsync();
        var hw = Hw();
        var rejected = await SeedActivationAsync(license, hw, ActivationStatus.Rejected);

        var result = await _service.ActivateAsync(new ActivateRequest
        {
            LicenseKey = license.LicenseKey,
            InstallGuid = Guid.NewGuid(),
            Hardware = hw,
        }, Ct);

        Assert.True(result.Success);
        Assert.Equal(ResultCode.PendingReview, result.Code);
        Assert.NotEqual(rejected.Id, result.Data!.ActivationId);
        Assert.Equal(2, await _db.Activations.CountAsync(Ct));
    }

    [Fact]
    public async Task Activate_MaxActivationsReached_RejectsWithoutCreatingActivation()
    {
        var license = await SeedLicenseAsync(maxActivations: 1);
        await SeedActivationAsync(license, Hw("1"), ActivationStatus.Approved);

        var result = await _service.ActivateAsync(new ActivateRequest
        {
            LicenseKey = license.LicenseKey,
            InstallGuid = Guid.NewGuid(),
            Hardware = Hw("2"),
        }, Ct);

        Assert.False(result.Success);
        Assert.Equal(ResultCode.MaxActivationsReached, result.Code);
        Assert.Equal(1, await _db.Activations.CountAsync(Ct));
    }

    [Fact]
    public async Task Activate_UnderMaxActivations_StillSucceeds()
    {
        var license = await SeedLicenseAsync(maxActivations: 2);
        await SeedActivationAsync(license, Hw("1"), ActivationStatus.Approved);

        var result = await _service.ActivateAsync(new ActivateRequest
        {
            LicenseKey = license.LicenseKey,
            InstallGuid = Guid.NewGuid(),
            Hardware = Hw("2"),
        }, Ct);

        Assert.True(result.Success);
        Assert.Equal(ResultCode.PendingReview, result.Code);
        Assert.Equal(2, await _db.Activations.CountAsync(Ct));
    }

    // ---------------------------------------------------------------------
    // CheckinAsync
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Checkin_UnknownActivationId_Fails()
    {
        var result = await _service.CheckinAsync(new CheckinRequest
        {
            ActivationId = Guid.NewGuid(),
            InstallGuid = Guid.NewGuid(),
            Hardware = Hw(),
        }, Ct);

        Assert.False(result.Success);
        Assert.Equal(ResultCode.ActivationNotFound, result.Code);
    }

    [Fact]
    public async Task Checkin_InstallGuidMismatch_Fails()
    {
        var license = await SeedLicenseAsync();
        var hw = Hw();
        var activation = await SeedActivationAsync(license, hw, ActivationStatus.Approved);

        var result = await _service.CheckinAsync(new CheckinRequest
        {
            ActivationId = activation.Id,
            InstallGuid = Guid.NewGuid(),
            Hardware = hw,
        }, Ct);

        Assert.False(result.Success);
        Assert.Equal(ResultCode.InstallGuidMismatch, result.Code);
    }

    [Fact]
    public async Task Checkin_RevokedLicense_ReturnsLocked()
    {
        var license = await SeedLicenseAsync(LicenseStatus.Revoked);
        var hw = Hw();
        var activation = await SeedActivationAsync(license, hw, ActivationStatus.Approved);

        var result = await _service.CheckinAsync(new CheckinRequest
        {
            ActivationId = activation.Id,
            InstallGuid = activation.InstallGuid,
            Hardware = hw,
        }, Ct);

        Assert.True(result.Success);
        Assert.Equal(ResultCode.Locked, result.Code);
        Assert.Equal("locked", result.Data!.Status);
        Assert.Equal("LICENSE_REVOKED", result.Data.Reason);
    }

    [Fact]
    public async Task Checkin_RejectedActivation_ReturnsLockedInsteadOfRenewing()
    {
        var license = await SeedLicenseAsync();
        var hw = Hw();
        var activation = await SeedActivationAsync(license, hw, ActivationStatus.Rejected);

        var result = await _service.CheckinAsync(new CheckinRequest
        {
            ActivationId = activation.Id,
            InstallGuid = activation.InstallGuid,
            Hardware = hw,
        }, Ct);

        Assert.True(result.Success);
        Assert.Equal(ResultCode.Locked, result.Code);
        Assert.Equal("ACTIVATION_REJECTED", result.Data!.Reason);
    }

    [Fact]
    public async Task Checkin_RevokedActivation_ReturnsLockedInsteadOfRenewing()
    {
        var license = await SeedLicenseAsync();
        var hw = Hw();
        var activation = await SeedActivationAsync(license, hw, ActivationStatus.Revoked);

        var result = await _service.CheckinAsync(new CheckinRequest
        {
            ActivationId = activation.Id,
            InstallGuid = activation.InstallGuid,
            Hardware = hw,
        }, Ct);

        Assert.True(result.Success);
        Assert.Equal(ResultCode.Locked, result.Code);
        Assert.Equal("ACTIVATION_REVOKED", result.Data!.Reason);
    }

    [Fact]
    public async Task Checkin_PendingReviewPastDeadline_ReturnsLockedGraceEnded()
    {
        var license = await SeedLicenseAsync();
        var hw = Hw();
        var activation = await SeedActivationAsync(license, hw, ActivationStatus.PendingReview,
            reviewDeadlineUtc: DateTime.UtcNow.AddDays(-1));

        var result = await _service.CheckinAsync(new CheckinRequest
        {
            ActivationId = activation.Id,
            InstallGuid = activation.InstallGuid,
            Hardware = hw,
        }, Ct);

        Assert.True(result.Success);
        Assert.Equal(ResultCode.Locked, result.Code);
        Assert.Equal("REVIEW_GRACE_ENDED", result.Data!.Reason);
    }

    [Fact]
    public async Task Checkin_PendingReviewWithinDeadline_Renews()
    {
        var license = await SeedLicenseAsync();
        var hw = Hw();
        var activation = await SeedActivationAsync(license, hw, ActivationStatus.PendingReview,
            reviewDeadlineUtc: DateTime.UtcNow.AddDays(1));

        var result = await _service.CheckinAsync(new CheckinRequest
        {
            ActivationId = activation.Id,
            InstallGuid = activation.InstallGuid,
            Hardware = hw,
        }, Ct);

        Assert.True(result.Success);
        Assert.Equal(ResultCode.Renewed, result.Code);
        Assert.Equal("pending_review", result.Data!.Status);
    }

    [Fact]
    public async Task Checkin_ApprovedSameHardware_Renews()
    {
        var license = await SeedLicenseAsync();
        var hw = Hw();
        var activation = await SeedActivationAsync(license, hw, ActivationStatus.Approved);

        var result = await _service.CheckinAsync(new CheckinRequest
        {
            ActivationId = activation.Id,
            InstallGuid = activation.InstallGuid,
            Hardware = hw,
        }, Ct);

        Assert.True(result.Success);
        Assert.Equal(ResultCode.Renewed, result.Code);
        Assert.Equal("approved", result.Data!.Status);
        Assert.False(string.IsNullOrEmpty(result.Data.LicenseFileBase64));
    }

    [Fact]
    public async Task Checkin_SubscriptionExpiredBeyondGrace_ReturnsLocked()
    {
        var license = await SeedLicenseAsync(subscriptionExpiryUtc: DateTime.UtcNow.AddDays(-31));
        var hw = Hw();
        var activation = await SeedActivationAsync(license, hw, ActivationStatus.Approved);

        var result = await _service.CheckinAsync(new CheckinRequest
        {
            ActivationId = activation.Id,
            InstallGuid = activation.InstallGuid,
            Hardware = hw,
        }, Ct);

        Assert.True(result.Success);
        Assert.Equal(ResultCode.Locked, result.Code);
        Assert.Equal("SUBSCRIPTION_EXPIRED_GRACE_ENDED", result.Data!.Reason);
    }

    [Fact]
    public async Task Checkin_SubscriptionExpiredWithinGrace_StillRenews()
    {
        var license = await SeedLicenseAsync(subscriptionExpiryUtc: DateTime.UtcNow.AddDays(-10));
        var hw = Hw();
        var activation = await SeedActivationAsync(license, hw, ActivationStatus.Approved);

        var result = await _service.CheckinAsync(new CheckinRequest
        {
            ActivationId = activation.Id,
            InstallGuid = activation.InstallGuid,
            Hardware = hw,
        }, Ct);

        Assert.True(result.Success);
        Assert.Equal(ResultCode.Renewed, result.Code);
    }

    [Fact]
    public async Task Checkin_HardwareDrift_ReopensReviewOnTheSameRowInsteadOfForkingOne()
    {
        // Regression test: the drift path used to build a synthetic ActivateRequest with
        // the SAME InstallGuid and hand it to ActivateAsync's "new install" branch, which
        // tried to INSERT a second Activation row for (license_id, install_guid) — a pair
        // that's UNIQUE in the schema and, since InstallGuid is stable across a hardware
        // change, already taken by this very row. That crashed with DbUpdateException on
        // the very first real hardware change, another path with zero prior test coverage.
        var license = await SeedLicenseAsync();
        var activation = await SeedActivationAsync(license, Hw("1"), ActivationStatus.Approved);

        var result = await _service.CheckinAsync(new CheckinRequest
        {
            ActivationId = activation.Id,
            InstallGuid = activation.InstallGuid,
            Hardware = Hw("2"),
        }, Ct);

        Assert.True(result.Success);
        Assert.Equal(ResultCode.PendingReview, result.Code);
        Assert.Equal(activation.Id, result.Data!.ActivationId);
        Assert.Equal(1, await _db.Activations.CountAsync(Ct));

        var reloaded = await _db.Activations.FirstAsync(a => a.Id == activation.Id, Ct);
        Assert.Equal(ActivationStatus.PendingReview, reloaded.Status);
        Assert.Equal("CPU-2", reloaded.CpuId);
        Assert.NotNull(reloaded.ReviewDeadlineUtc);
    }

    [Fact]
    public async Task Activate_SameInstallGuidAsARejectedActivation_ReopensReviewOnThatRowInsteadOfCrashing()
    {
        // Regression test for a real production 500: reported live as a rejected
        // activation's InstallGuid calling /api/activate again with the SAME (already
        // on file, now-rejected) hardware. ActivateAsync's "no match" branch — match is
        // null because FindMatchingActivation excludes Rejected — tried to INSERT a new
        // Activation row reusing that InstallGuid, which already belongs to the rejected
        // row for this license. (LicenseId, InstallGuid) is UNIQUE, so this always threw
        // DbUpdateException/23505 in production, surfaced to the client as ServerError.
        var license = await SeedLicenseAsync(maxActivations: 5);
        var hw = Hw("2");
        var rejected = await SeedActivationAsync(license, hw, ActivationStatus.Rejected);

        var result = await _service.ActivateAsync(new ActivateRequest
        {
            LicenseKey = license.LicenseKey,
            InstallGuid = rejected.InstallGuid,
            Hardware = hw,
        }, Ct);

        Assert.True(result.Success);
        Assert.Equal(ResultCode.PendingReview, result.Code);
        Assert.Equal(rejected.Id, result.Data!.ActivationId);
        Assert.Equal(1, await _db.Activations.CountAsync(Ct));

        var reloaded = await _db.Activations.FirstAsync(a => a.Id == rejected.Id, Ct);
        Assert.Equal(ActivationStatus.PendingReview, reloaded.Status);
        Assert.Null(reloaded.RejectedAtUtc);
    }

    [Fact]
    public async Task Activate_SameInstallGuidDifferentHardwareThanItsApprovedRecord_ReopensReviewOnThatRowInsteadOfCrashing()
    {
        // Same root cause as above, reached via a plain hardware change on an existing
        // Approved activation through a direct /activate call rather than /checkin.
        var license = await SeedLicenseAsync(maxActivations: 5);
        var approved = await SeedActivationAsync(license, Hw("1"), ActivationStatus.Approved);

        var result = await _service.ActivateAsync(new ActivateRequest
        {
            LicenseKey = license.LicenseKey,
            InstallGuid = approved.InstallGuid,
            Hardware = Hw("2"),
        }, Ct);

        Assert.True(result.Success);
        Assert.Equal(ResultCode.PendingReview, result.Code);
        Assert.Equal(approved.Id, result.Data!.ActivationId);
        Assert.Equal(1, await _db.Activations.CountAsync(Ct));

        var reloaded = await _db.Activations.FirstAsync(a => a.Id == approved.Id, Ct);
        Assert.Equal(ActivationStatus.PendingReview, reloaded.Status);
        Assert.Equal("CPU-2", reloaded.CpuId);
    }
}
