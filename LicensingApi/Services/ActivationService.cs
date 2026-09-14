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
        string logAction;

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
            logAction = "Reactivated";
        }
        else
        {
            // No hardware match on record for this license. This InstallGuid may still
            // already own a row here — e.g. it was Rejected/Revoked (FindMatchingActivation
            // deliberately excludes those from hardware matching), or its hardware simply
            // drifted since a direct (non-checkin) /activate call. Either way, a second row
            // for this (LicenseId, InstallGuid) pair can never be inserted — it's UNIQUE,
            // and the client's InstallGuid is stable — so re-open review on that SAME row
            // instead of trying (and failing) to create a new one. Only when this
            // InstallGuid has never been seen before is this genuinely a new install.
            var existingForInstall = license.Activations.FirstOrDefault(a => a.InstallGuid == req.InstallGuid);
            if (existingForInstall is not null)
            {
                existingForInstall.CpuId = req.Hardware.CpuId;
                existingForInstall.MotherboardSerial = req.Hardware.MotherboardSerial;
                existingForInstall.TpmId = req.Hardware.TpmId;
                existingForInstall.MacAddressPrimary = req.Hardware.MacAddressPrimary;
                existingForInstall.Status = ActivationStatus.PendingReview;
                existingForInstall.ReviewDeadlineUtc = DateTime.UtcNow.AddDays(ReviewGraceDays);
                existingForInstall.ApprovedAtUtc = null;
                existingForInstall.RejectedAtUtc = null;
                existingForInstall.ReviewNotes = null;
                ApplyEnvironmentInfo(existingForInstall, req.Hardware, req.Vm);

                activation = existingForInstall;
                logAction = "HardwareDrift";
            }
            else
            {
                var approvedCount = license.Activations.Count(a => a.Status == ActivationStatus.Approved);
                if (approvedCount >= license.MaxActivations)
                    return ApiResult.Fail(ResultCode.MaxActivationsReached,
                        $"Maximum number of active installations ({license.MaxActivations}) reached for this license.");

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

                _db.Activations.Add(activation);
                logAction = "Created";
            }
        }

        await LogAsync("system", "Activation", activation.Id.ToString(), logAction, ct);

        await _db.SaveChangesAsync(ct);

        var fileStatus = activation.Status == ActivationStatus.Approved ? "approved" : "pending_review";
        return BuildResult(activation, license,
            activation.Status == ActivationStatus.Approved ? ResultCode.Activated : ResultCode.PendingReview,
            fileStatus);
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

        if (activation.Status is ActivationStatus.Rejected or ActivationStatus.Revoked)
            return await LockedResultAsync(activation, license,
                activation.Status == ActivationStatus.Rejected ? "ACTIVATION_REJECTED" : "ACTIVATION_REVOKED", ct);

        if (activation.Status == ActivationStatus.PendingReview
            && activation.ReviewDeadlineUtc is { } deadline
            && DateTime.UtcNow > deadline)
            return await LockedResultAsync(activation, license, "REVIEW_GRACE_ENDED", ct);

        // Hardware drift check: if it no longer matches, this check-in re-opens review
        // on the SAME activation row. It cannot fork a new row under the same
        // InstallGuid — activations.(license_id, install_guid) is UNIQUE, and the DLL's
        // InstallGuid is stable across a hardware change (it identifies the install,
        // not the machine), so a second row would always collide on that constraint.
        if (!_hwMatch.IsSameMachine(activation, req.Hardware))
        {
            activation.CpuId = req.Hardware.CpuId;
            activation.MotherboardSerial = req.Hardware.MotherboardSerial;
            activation.TpmId = req.Hardware.TpmId;
            activation.MacAddressPrimary = req.Hardware.MacAddressPrimary;
            activation.Status = ActivationStatus.PendingReview;
            activation.ReviewDeadlineUtc = DateTime.UtcNow.AddDays(ReviewGraceDays);
            activation.ApprovedAtUtc = null;
            activation.RejectedAtUtc = null;
            activation.ReviewNotes = null;
            ApplyEnvironmentInfo(activation, req.Hardware, req.Vm);

            await LogAsync("system", "Activation", activation.Id.ToString(), "HardwareDrift", ct);
            await _db.SaveChangesAsync(ct);

            return BuildResult(activation, license, ResultCode.PendingReview, "pending_review");
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

        var status = activation.Status == ActivationStatus.Approved ? "approved" : "pending_review";
        return BuildResult(activation, license, ResultCode.Renewed, status);
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

    private ApiResult BuildResult(Activation activation, License license, ResultCode code, string status)
    {
        var policy = new PolicyDto
        {
            CheckIntervalHours = DefaultCheckIntervalHours,
            GraceDays = ReviewGraceDays,
            SubscriptionGraceDays = SubscriptionGraceDays,
        };

        var licenseFile = _fileService.BuildSignedFile(new LicenseFilePayload
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

        return ApiResult.Ok(code, new ActivationResultData
        {
            ActivationId = activation.Id,
            Status = status,
            LicenseFileBase64 = licenseFile,
            Policy = policy,
            SubscriptionExpiryUtc = license.SubscriptionExpiryUtc,
            ReviewDeadlineUtc = activation.ReviewDeadlineUtc,
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
