using LicensingApi.Dtos;
using LicensingApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace LicensingApi.Controllers;

[ApiController]
[Route("api")]
public class ActivationController : ControllerBase
{
    private readonly ActivationService _service;
    private readonly ILogger<ActivationController> _logger;

    public ActivationController(ActivationService service, ILogger<ActivationController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost("activate")]
    public async Task<ActionResult<ApiResult>> Activate([FromBody] ActivateRequest request, CancellationToken ct)
    {
        // TODO: rate limiting middleware (per license key / per IP) should sit in front
        // of this endpoint before production — see design doc §7 Security Considerations.
        try
        {
            var result = await _service.ActivateAsync(request, ct);
            return result.Success ? Ok(result) : MapFailureStatusCode(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in /activate for key {Key}", request.LicenseKey);
            return StatusCode(500, ApiResult.Fail(ResultCode.ServerError, "An unexpected error occurred."));
        }
    }

    [HttpPost("checkin")]
    public async Task<ActionResult<ApiResult>> Checkin([FromBody] CheckinRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _service.CheckinAsync(request, ct);
            return result.Success ? Ok(result) : MapFailureStatusCode(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in /checkin for activation {Id}", request.ActivationId);
            return StatusCode(500, ApiResult.Fail(ResultCode.ServerError, "An unexpected error occurred."));
        }
    }

    private ActionResult<ApiResult> MapFailureStatusCode(ApiResult result)
    {
        var statusCode = result.Code switch
        {
            ResultCode.LicenseNotFound or ResultCode.ActivationNotFound => 404,
            ResultCode.InvalidKeyFormat => 400,
            ResultCode.LicenseRevoked or ResultCode.LicenseExpired or ResultCode.InstallGuidMismatch => 403,
            ResultCode.MaxActivationsReached => 409,
            ResultCode.RateLimited => 429,
            _ => 400,
        };
        return StatusCode(statusCode, result);
    }
}
