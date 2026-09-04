using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Ingestion.Registry;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Freebuff.Platform.Api.Controllers;

/// <summary>
/// Device telemetry ingestion. Anonymous by design — devices cannot hold JWTs.
/// The vendor code in the route resolves the adapter; a per-vendor ingest key
/// (DeviceVendor.Metadata.ingestKey) authenticates the feed when configured, and
/// the device must be registered + Active (trusted server-side lookup — the
/// client never supplies company/vehicle ids). Status codes:
///  200 accepted • 401 bad/missing ingest key • 404 unknown vendor/device
///  400 malformed/unregistered-device payloads.
/// </summary>
[ApiController]
[Route("api/v1/ingest")]
public class IngestController : ControllerBase
{
    private readonly DeviceIngestionService _ingestion;
    private readonly VendorAdapterRegistry _registry;

    public IngestController(DeviceIngestionService ingestion, VendorAdapterRegistry registry)
    {
        _ingestion = ingestion;
        _registry = registry;
    }

    [HttpPost("{vendorCode}")]
    [AllowAnonymous]
    [RequestSizeLimit(256 * 1024)]
    public async Task<ActionResult<ApiResponse<IngestResult>>> Post(string vendorCode)
    {
        var payload = await ReadBodyAsync();
        var ingestKey = Request.Headers.TryGetValue("X-Ingest-Key", out var key) ? key.ToString() : null;
        var contentType = Request.ContentType;

        var result = await _ingestion.IngestAsync(vendorCode, $"http:/{Request.Path}", payload, contentType, ingestKey);

        return result.Code switch
        {
            "UNAUTHORIZED" => Unauthorized(ApiResponse<IngestResult>.Fail(result.Code, result.Message)),
            "UNKNOWN_VENDOR" or "VENDOR_INACTIVE" or "DEVICE_NOT_REGISTERED" => NotFound(ApiResponse<IngestResult>.Fail(result.Code, result.Message)),
            _ when !result.Accepted => BadRequest(ApiResponse<IngestResult>.Fail(result.Code, result.Message)),
            _ => Ok(ApiResponse<IngestResult>.Ok(result))
        };
    }

    [HttpGet]
    [AllowAnonymous]
    public ActionResult<ApiResponse<object>> Catalog()
        => Ok(ApiResponse<object>.Ok(new
        {
            vendors = _registry.All.Select(a => new { a.VendorCode, a.ProtocolType, a.PayloadFormat }).ToList()
        }));

    private async Task<byte[]> ReadBodyAsync()
    {
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms);
        return ms.ToArray();
    }
}
