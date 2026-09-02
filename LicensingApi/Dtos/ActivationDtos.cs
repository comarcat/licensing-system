namespace LicensingApi.Dtos;

public class HardwareInfo
{
    public required string CpuId { get; set; }
    public required string MotherboardSerial { get; set; }
    public required string TpmId { get; set; }
    public required string MacAddressPrimary { get; set; }
    public string? OsType { get; set; }
    public string? OsVersion { get; set; }
    public string? CpuModel { get; set; }
    public int? RamGb { get; set; }
}

public class VmInfo
{
    public bool HypervisorPresent { get; set; }
    public List<string> Signals { get; set; } = new();
}

public class ActivateRequest
{
    public required string LicenseKey { get; set; }
    public required Guid InstallGuid { get; set; }
    public required HardwareInfo Hardware { get; set; }
    public VmInfo? Vm { get; set; }
    public string? AppVersion { get; set; }
    public DateTime ClientTimestampUtc { get; set; }
}

public class CheckinRequest
{
    public required Guid ActivationId { get; set; }
    public required Guid InstallGuid { get; set; }
    public required HardwareInfo Hardware { get; set; }
    public VmInfo? Vm { get; set; }
    public string? LastLocalStatus { get; set; }
    public DateTime ClientTimestampUtc { get; set; }
}

public class PolicyDto
{
    public int CheckIntervalHours { get; set; }
    public int GraceDays { get; set; }
    public int SubscriptionGraceDays { get; set; }
}

public class ActivationResultData
{
    public Guid? ActivationId { get; set; }
    public string Status { get; set; } = ""; // "approved" | "pending_review" | "locked" | "rejected"
    public string? LicenseFileBase64 { get; set; }
    public PolicyDto? Policy { get; set; }
    public DateTime? SubscriptionExpiryUtc { get; set; }
    public DateTime? ReviewDeadlineUtc { get; set; }
    public string? Reason { get; set; } // extra machine-readable detail for Locked/PendingReview
}

public class ApiResult
{
    public bool Success { get; set; }
    public required ResultCode Code { get; set; }
    public string? Message { get; set; }
    public ActivationResultData? Data { get; set; }

    public static ApiResult Ok(ResultCode code, ActivationResultData data) =>
        new() { Success = true, Code = code, Data = data };

    public static ApiResult Fail(ResultCode code, string message) =>
        new() { Success = false, Code = code, Message = message };
}
