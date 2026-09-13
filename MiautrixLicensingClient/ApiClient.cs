using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Miautrix.Licensing.Client;

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

public class ActivateRequest
{
    public required string LicenseKey { get; set; }
    public required Guid InstallGuid { get; set; }
    public required HardwareInfo Hardware { get; set; }
    public string? AppVersion { get; set; }
    public DateTime ClientTimestampUtc { get; set; }
}

public class CheckinRequest
{
    public required Guid ActivationId { get; set; }
    public required Guid InstallGuid { get; set; }
    public required HardwareInfo Hardware { get; set; }
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
/// Mirrors LicensingApi.Dtos.ResultCode. The client should switch on this rather than
/// parsing Message text, and treat any value it doesn't recognize as a failure — future
/// server versions may add codes.
/// </summary>
public enum ResultCode
{
    Activated, PendingReview, Renewed, Locked,
    InvalidKeyFormat, LicenseNotFound, LicenseExpired, LicenseRevoked,
    MaxActivationsReached, InstallGuidMismatch, ActivationNotFound, RateLimited, ServerError,
}

/// <summary>
/// Thin HTTP client for LicensingApi's two activation endpoints. See
/// activation-dll-integration-reference.md for the full contract (business rules,
/// error codes, license-file decryption).
/// </summary>
public sealed class ActivationApiClient : IDisposable
{
    private readonly HttpClient _http;

    /// <param name="baseUrl">
    /// e.g. "https://licensing-api.miautrix.tech" (production) or
    /// "http://10.11.1.41:8080" (LAN-direct).
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
        var result = JsonSerializer.Deserialize<ApiResult>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? new ApiResult { Success = false, Code = ResultCode.ServerError, Message = "Empty/unparseable response." };
        return ((int)response.StatusCode, result);
    }

    public void Dispose() => _http.Dispose();
}
