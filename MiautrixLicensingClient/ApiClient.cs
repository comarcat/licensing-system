using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Miautrix.Licensing.Client;

public enum LicenseStatus
{
    Active = 0,
    Revoked = 1,
    Expired = 2,
    Test = 3,
}

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

    /// <summary>
    /// Optional customer email to store on the license record during activation.
    /// </summary>
    public string? Email { get; set; }

    public DateTime ClientTimestampUtc { get; set; }

    /// <summary>
    /// Product version this install is activating. Optional and retrocompatible: omit it
    /// (or send <see cref="Guid.Empty"/>) when the integration does not pin a product
    /// version. If supplied and it differs from the version stored on the license, the
    /// server rejects activation with <see cref="ResultCode.InvalidKeyFormat"/>.
    /// </summary>
    public Guid? VersionId { get; set; }

    /// <summary>
    /// Optional status override for controlled test scenarios.
    /// </summary>
    public LicenseStatus? Status { get; set; }
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
    public string Status { get; set; } = "";
    public string? LicenseFileBase64 { get; set; }
    public PolicyDto? Policy { get; set; }
    public DateTime? SubscriptionExpiryUtc { get; set; }
    public DateTime? ReviewDeadlineUtc { get; set; }
    public string? Reason { get; set; }
}

public class ApiResult
{
    public bool Success { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ResultCode Code { get; set; }
    public string? Message { get; set; }
    public ActivationResultData? Data { get; set; }
}

/// <summary>
/// Mirrors LicensingApi.Dtos.ResultCode. Switch on this value instead of parsing
/// human-readable messages. Unknown future values should be treated as failures.
/// </summary>
public enum ResultCode
{
    Activated, PendingReview, Renewed, Locked,
    InvalidKeyFormat, LicenseNotFound, LicenseExpired, LicenseRevoked,
    MaxActivationsReached, InstallGuidMismatch, ActivationNotFound, RateLimited, ServerError,
}

/// <summary>
/// Thin HTTP client for LicensingApi's activation endpoints.
///
/// Current activation behavior:
/// - Existing/same hardware reuses the activation record.
/// - New hardware auto-approves while approved activations are below MaxActivations.
/// - New hardware beyond MaxActivations returns PendingReview.
/// - Test licenses bypass activation limits.
/// </summary>
public sealed class ActivationApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    /// <param name="baseUrl">
    /// Example: "https://licensing-api.miautrix.tech".
    /// </param>
    public ActivationApiClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<(int HttpStatus, ApiResult Result)> ActivateAsync(ActivateRequest request, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync("/api/activate", request, ct);
        return await ReadResultAsync(response, ct);
    }

    public async Task<(int HttpStatus, ApiResult Result)> CheckinAsync(CheckinRequest request, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync("/api/checkin", request, ct);
        return await ReadResultAsync(response, ct);
    }

    private static async Task<(int, ApiResult)> ReadResultAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        try
        {
            var result = JsonSerializer.Deserialize<ApiResult>(body, JsonOptions)
                ?? new ApiResult { Success = false, Code = ResultCode.ServerError, Message = "Empty response." };
            return ((int)response.StatusCode, result);
        }
        catch (JsonException)
        {
            return ((int)response.StatusCode, new ApiResult
            {
                Success = false,
                Code = ResultCode.ServerError,
                Message = string.IsNullOrWhiteSpace(body)
                    ? "Empty/unparseable response."
                    : body.Trim(),
            });
        }
    }

    public void Dispose() => _http.Dispose();
}
