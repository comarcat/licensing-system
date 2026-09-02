using System.Text.RegularExpressions;
using LicensingCore.Data;
using LicensingApi.Dtos;
using LicensingCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace LicensingApi.Services;

public partial class ActivationService
{
    private readonly AppDbContext _db;
    private readonly IHardwareMatchService _hwMatch;
    private readonly ILicenseFileService _fileService;

    private const int ReviewGraceDays = 15;
    private const int SubscriptionGraceDays = 30;
    private const int DefaultCheckIntervalHours = 6;

    [GeneratedRegex(@"^[A-Z0-9]{4}-[A-Z0-9]{5}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{2}$")]
    private static partial Regex LicenseKeyFormat();

    public ActivationService(AppDbContext db, IHardwareMatchService hwMatch, ILicenseFileService fileService)
    {
        _db = db;
        _hwMatch = hwMatch;
        _fileService = fileService;
    }

    public async Task<ApiResult> ActivateAsync(ActivateRequest req, CancellationToken ct = default)
    {
        if (!LicenseKeyFormat().IsMatch(req.LicenseKey))
            return ApiResult.Fail(ResultCode.InvalidKeyFormat, "License key format is invalid.");

        var license = await _db.Licenses
            .Include(l => l.Activations)
            .Include(l => l.Product)
            .FirstOrDefaultAsync(l => l.LicenseKey == req.LicenseKey, ct);

        if (license is null)
            return ApiResult.Fail(ResultCode.LicenseNotFound, "No license found for this key.");

        if (license.Status == LicenseStatus.Revoked)
            return ApiResult.Fail(ResultCode.LicenseRevoked, "This license has been revoked.");

        if (license.Status == LicenseStatus.Expired)
            return ApiResult.Fail(ResultCode.LicenseExpired, "This license has expired.");

        var match = _hwMatch.FindMatchingActivation(license.Activations, req.Hardware);

        Activation activation;
        bool isNewInstall;

        if (match is not null)
        {
            // Same machine reactivating (possibly with a new/refreshed install GUID).
            match.InstallGuid = req.InstallGuid;
            match.LastCheckinAtUtc = DateTime.UtcNow;
            ApplyEnvironmentInfo(match, req.Hardware, req.Vm);
            if (match.Status == ActivationStatus.PendingReview)
            {
                // Still pending admin approval — don't silently auto-approve.
            }
            else
            {
                match.Status = ActivationStatus.Approved;
            }
            activation = match;
            isNewInstall = false;
        }
        else
        {
            // No hardware match on record for this license: new install or a changed machine.
            activation = new Activation
            {
                Id = Guid.NewGuid(),
                LicenseId = license.Id,
                InstallGuid = req.InstallGuid,
                CpuId = req.Hardware.CpuId,
                MotherboardSerial = req.Hardware.MotherboardSerial,
                TpmId = req.Hardware.TpmId,
                MacAddressPrimary = req.Hardware.MacAddressPrimary,
                Status = ActivationStatus.PendingReview,
                FirstActivatedAtUtc = DateTime.UtcNow,
                ReviewDeadlineUtc = DateTime.UtcNow.AddDays(ReviewGraceDays),
            };
            ApplyEnvironmentInfo(activation, req.Hardware, req.Vm);

            var approvedCount = license.Activations.Count(a => a.Status == ActivationStatus.Approved);
            if (approvedCount >= license.MaxActivations)
            {
                activation.ReviewNotes = $"Max activations ({license.MaxActivations}) already reached; review carefully.";
            }

            _db.Activations.Add(activation);
            isNewInstall = true;
        }

        await LogAsync("system", "Activation", activation.Id.ToString(),
            isNewInstall ? "Created" : "Reactivated", ct);

        await _db.SaveChangesAsync(ct);

        var policy = new PolicyDto
        {
            CheckIntervalHours = DefaultCheckIntervalHours,
            GraceDays = ReviewGraceDays,
            SubscriptionGraceDays = SubscriptionGraceDays,
        };

        var fileStatus = activation.Status == ActivationStatus.Approved ? "approved" : "pending_review";
        var licenseFile = _fileService.BuildSignedEncryptedFile(new LicenseFilePayload
        {
            LicenseKey = license.LicenseKey,
            ActivationId = activation.Id,
            InstallGuid = activation.InstallGuid,
            Status = fileStatus,
            CpuId = activation.CpuId,
            MotherboardSerial = activation.MotherboardSerial,
            TpmId = activation.TpmId,
            MacAddressPrimary = activation.MacAddressPrimary,
            CheckIntervalHours = policy.CheckIntervalHours,
            GraceDays = policy.GraceDays,
            SubscriptionGraceDays = policy.SubscriptionGraceDays,
            SubscriptionExpiryUtc = license.SubscriptionExpiryUtc,
        });

        var data = new ActivationResultData
        {
            ActivationId = activation.Id,
            Status = fileStatus,
            LicenseFileBase64 = licenseFile,
            Policy = policy,
            SubscriptionExpiryUtc = license.SubscriptionExpiryUtc,
            ReviewDeadlineUtc = activation.ReviewDeadlineUtc,
        };

        return ApiResult.Ok(
            activation.Status == ActivationStatus.Approved ? ResultCode.Activated : ResultCode.PendingReview,
            data);
    }

    public async Task<ApiResult> CheckinAsync(CheckinRequest req, CancellationToken ct = default)
    {
        var activation = await _db.Activations
            .Include(a => a.License)
            .ThenInclude(l => l.Activations)
            .FirstOrDefaultAsync(a => a.Id == req.ActivationId, ct);

        if (activation is null)
            return ApiResult.Fail(ResultCode.ActivationNotFound, "Unknown activation id.");

        if (activation.InstallGuid != req.InstallGuid)
            return ApiResult.Fail(ResultCode.InstallGuidMismatch, "Install GUID does not match this activation.");

        var license = activation.License;

        if (license.Status == LicenseStatus.Revoked)
            return await LockedResultAsync(activation, license, "LICENSE_REVOKED", ct);

        // Hardware drift check: if it no longer matches, this check-in itself creates
        // a new pending-review activation (same rule as a fresh /activate mismatch).
        if (!_hwMatch.IsSameMachine(activation, req.Hardware))
        {
            var reactivateReq = new ActivateRequest
            {
                LicenseKey = license.LicenseKey,
                InstallGuid = req.InstallGuid,
                Hardware = req.Hardware,
                Vm = req.Vm,
                ClientTimestampUtc = req.ClientTimestampUtc,
            };
            return await ActivateAsync(reactivateReq, ct);
        }

        // Subscription expiry / grace handling.
        if (license.SubscriptionExpiryUtc is { } expiry)
        {
            var graceEnd = expiry.AddDays(SubscriptionGraceDays);
            if (DateTime.UtcNow > graceEnd)
                return await LockedResultAsync(activation, license, "SUBSCRIPTION_EXPIRED_GRACE_ENDED", ct);
        }

        activation.LastCheckinAtUtc = DateTime.UtcNow;
        ApplyEnvironmentInfo(activation, req.Hardware, req.Vm);
        await _db.SaveChangesAsync(ct);

        var policy = new PolicyDto
        {
            CheckIntervalHours = DefaultCheckIntervalHours,
            GraceDays = ReviewGraceDays,
            SubscriptionGraceDays = SubscriptionGraceDays,
        };

        var status = activation.Status == ActivationStatus.Approved ? "approved" : "pending_review";
        var licenseFile = _fileService.BuildSignedEncryptedFile(new LicenseFilePayload
        {
            LicenseKey = license.LicenseKey,
            ActivationId = activation.Id,
            InstallGuid = activation.InstallGuid,
            Status = status,
            CpuId = activation.CpuId,
            MotherboardSerial = activation.MotherboardSerial,
            TpmId = activation.TpmId,
            MacAddressPrimary = activation.MacAddressPrimary,
            CheckIntervalHours = policy.CheckIntervalHours,
            GraceDays = policy.GraceDays,
            SubscriptionGraceDays = policy.SubscriptionGraceDays,
            SubscriptionExpiryUtc = license.SubscriptionExpiryUtc,
        });

        return ApiResult.Ok(ResultCode.Renewed, new ActivationResultData
        {
            ActivationId = activation.Id,
            Status = status,
            LicenseFileBase64 = licenseFile,
            Policy = policy,
            SubscriptionExpiryUtc = license.SubscriptionExpiryUtc,
        });
    }

    private async Task<ApiResult> LockedResultAsync(Activation activation, License license, string reason, CancellationToken ct)
    {
        await LogAsync("system", "Activation", activation.Id.ToString(), $"Locked:{reason}", ct);
        await _db.SaveChangesAsync(ct);

        return ApiResult.Ok(ResultCode.Locked, new ActivationResultData
        {
            ActivationId = activation.Id,
            Status = "locked",
            Reason = reason,
        });
    }

    private static void ApplyEnvironmentInfo(Activation a, HardwareInfo hw, VmInfo? vm)
    {
        a.OsType = hw.OsType;
        a.OsVersion = hw.OsVersion;
        a.CpuModel = hw.CpuModel;
        a.RamGb = hw.RamGb;
        if (vm is not null)
        {
            a.IsVm = vm.HypervisorPresent || vm.Signals.Count > 0;
            a.VmSignals = vm.Signals.Count > 0 ? string.Join(",", vm.Signals) : null;
        }
    }

    private async Task LogAsync(string actor, string entityType, string entityId, string action, CancellationToken ct)
    {
        _db.AuditLogEntries.Add(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            Actor = actor,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
        });
        await Task.CompletedTask;
    }
}
