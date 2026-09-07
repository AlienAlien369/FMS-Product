using Freebuff.Platform.Api.Authorization;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Shared.Extensions;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Controllers;

/// <summary>
/// Proof-of-delivery evidence for a trip's waypoints (capture + viewing). The
/// trip is the tenant boundary: every endpoint first verifies the caller owns
/// the trip (or is SuperAdmin) via <see cref="OwnsTripAsync"/> — one guard for
/// all six surfaces, so isolation can't drift endpoint by endpoint.
///
/// Capture is gated on pod.create, viewing on pod.view (roles without grants
/// get 403). Geolocation mismatch is a data-quality flag, never a hard block.
/// The evidence list endpoints double as the read surface for the future
/// customer tracking link.
/// </summary>
[ApiController]
[Route("api/v1/trips/{id:guid}")]
[Authorize]
public class ProofOfDeliveryController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ProofOfDeliveryService _podService;

    public ProofOfDeliveryController(ApplicationDbContext db, ProofOfDeliveryService podService)
    {
        _db = db;
        _podService = podService;
    }

    /// <summary>Single tenant guard for every POD surface: the caller must own the trip (or be SuperAdmin).</summary>
    private Task<bool> OwnsTripAsync(Guid id)
        => _db.Trips.AsNoTracking()
            .AnyAsync(t => t.Id == id && !t.IsDeleted && (User.IsSuperAdmin() || t.CompanyId == User.GetTenantId()));

    private async Task<IActionResult?> GuardTripAsync(Guid id)
        => await OwnsTripAsync(id) ? null : NotFound(new ApiResponse<object> { Success = false, Message = "Trip not found." });

    /// <summary>All POD evidence for a trip (evidence panel / customer tracking link).</summary>
    [HttpGet("pod")]
    [RequirePermission("pod.view")]
    public async Task<IActionResult> GetTripPod(Guid id)
    {
        var guard = await GuardTripAsync(id);
        if (guard != null) return guard;

        var names = await _db.TripWaypoints.AsNoTracking()
            .Where(w => w.TripId == id && !w.IsDeleted)
            .ToDictionaryAsync(w => w.Id, w => w.Name);
        var records = await _podService.ListForTripAsync(id);
        return Ok(new ApiResponse<List<ProofOfDeliveryDto>>
        {
            Success = true,
            Data = records.Select(p => ToPodDto(p, names.GetValueOrDefault(p.WaypointId, p.WaypointId.ToString()))).ToList()
        });
    }

    /// <summary>POD evidence for one waypoint.</summary>
    [HttpGet("waypoints/{waypointId:guid}/pod")]
    [RequirePermission("pod.view")]
    public async Task<IActionResult> GetWaypointPod(Guid id, Guid waypointId)
    {
        var guard = await GuardTripAsync(id);
        if (guard != null) return guard;

        var name = await _db.TripWaypoints.AsNoTracking()
            .Where(w => w.Id == waypointId && w.TripId == id && !w.IsDeleted)
            .Select(w => w.Name)
            .FirstOrDefaultAsync();
        if (name == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Waypoint not found on this trip." });

        var records = await _podService.ListForWaypointAsync(id, waypointId);
        return Ok(new ApiResponse<List<ProofOfDeliveryDto>>
        {
            Success = true,
            Data = records.Select(p => ToPodDto(p, name)).ToList()
        });
    }

    /// <summary>Capture a signature at a delivery waypoint.</summary>
    [HttpPost("waypoints/{waypointId:guid}/pod/signature")]
    [RequirePermission("pod.create")]
    public async Task<IActionResult> CapturePodSignature(Guid id, Guid waypointId, [FromBody] CreatePodSignatureDto dto)
    {
        var guard = await GuardTripAsync(id);
        if (guard != null) return guard;

        try
        {
            var record = await _podService.CaptureSignatureAsync(id, waypointId, User.GetUserIdString(),
                dto.SignatureSvg, dto.Latitude, dto.Longitude, dto.Notes);
            return Ok(new ApiResponse<ProofOfDeliveryDto>
            {
                Success = true,
                Message = "Proof of delivery captured.",
                Data = ToPodDto(record, await WaypointNameAsync(id, waypointId))
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiResponse<object> { Success = false, Message = ex.Message });
        }
    }

    /// <summary>
    /// Capture a photo at a delivery waypoint. The file itself is uploaded here as
    /// multipart (image content-type, ≤ 5 MB), written to the config-driven uploads
    /// directory under a server-generated name, and the POD record stores the served
    /// URL reference — never multi-MB base64 in the row. Legacy records whose
    /// ImageUrl holds a base64 data URL keep rendering (the viewer renders both).
    /// </summary>
    [HttpPost("waypoints/{waypointId:guid}/pod/photo")]
    [RequirePermission("pod.create")]
    public async Task<IActionResult> CapturePodPhoto(Guid id, Guid waypointId, [FromForm] UploadPodPhotoDto dto)
    {
        var guard = await GuardTripAsync(id);
        if (guard != null) return guard;

        var file = dto.File;
        var (valid, error) = ProofOfDeliveryService.ValidatePhotoUpload(file?.ContentType, file?.Length ?? 0);
        if (!valid || file == null)
            return BadRequest(new ApiResponse<object> { Success = false, Message = error ?? "A photo upload is required." });

        try
        {
            var url = await _podService.StorePhotoAsync(id, file.ContentType, file.OpenReadStream());
            var record = await _podService.CapturePhotoAsync(id, waypointId, User.GetUserIdString(),
                url, dto.Latitude, dto.Longitude, dto.Notes);
            return Ok(new ApiResponse<ProofOfDeliveryDto>
            {
                Success = true,
                Message = "Proof of delivery captured.",
                Data = ToPodDto(record, await WaypointNameAsync(id, waypointId))
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiResponse<object> { Success = false, Message = ex.Message });
        }
    }

    /// <summary>
    /// Serves a stored POD photo back (the reference stored in ImageUrl). Guarded by
    /// the same trip-ownership check as every POD surface, so a photo URL can't leak
    /// across tenants. Browsers can't set Authorization on an &lt;img&gt; request, so the
    /// JWT rides as access_token — the same convention the SignalR hub uses.
    /// </summary>
    [HttpGet("pod/photos/{fileName}")]
    [RequirePermission("pod.view")]
    public async Task<IActionResult> GetPodPhoto(Guid id, string fileName)
    {
        var guard = await GuardTripAsync(id);
        if (guard != null) return guard;

        var (fullPath, contentType) = _podService.ResolvePhoto(id, fileName);
        if (fullPath == null)
            return NotFound(new ApiResponse<object> { Success = false, Message = "Photo not found." });
        return PhysicalFile(fullPath, contentType ?? "application/octet-stream", enableRangeProcessing: true);
    }

    private async Task<string> WaypointNameAsync(Guid tripId, Guid waypointId)
        => await _db.TripWaypoints.AsNoTracking()
            .Where(w => w.Id == waypointId && w.TripId == tripId).Select(w => w.Name).FirstOrDefaultAsync()
            ?? waypointId.ToString();

    /// <summary>
    /// Issue an OTP for the waypoint's customer (sent via SMS/email by the
    /// delivery gateway — dev returns the code in the response so the capture
    /// flow can relay it). Only the SHA-256 hash is stored.
    /// </summary>
    [HttpPost("waypoints/{waypointId:guid}/pod/otp")]
    [RequirePermission("pod.create")]
    public async Task<IActionResult> SendPodOtp(Guid id, Guid waypointId)
    {
        var guard = await GuardTripAsync(id);
        if (guard != null) return guard;

        var wp = await _db.TripWaypoints.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == waypointId && w.TripId == id && !w.IsDeleted);
        if (wp == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Waypoint not found on this trip." });

        var (record, otp) = await _podService.SendOtpAsync(id, waypointId, User.GetUserIdString());
        var customerChannel = wp.CustomerPhone ?? wp.CustomerEmail;
        return Ok(new ApiResponse<object>
        {
            Success = true,
            Message = customerChannel == null
                ? "OTP issued — add a customer phone/email to this waypoint for SMS/email delivery."
                : $"OTP issued for delivery to {customerChannel} (SMS/email gateway integration).",
            Data = new
            {
                record.Id,
                Otp = otp, // dev-mode delivery: production relays via the SMS/email gateway, never returned
                CustomerChannel = customerChannel
            }
        });
    }

    /// <summary>Verify the customer's OTP against the issued hash; stamps verification + capture geolocation.</summary>
    [HttpPost("waypoints/{waypointId:guid}/pod/otp/verify")]
    [RequirePermission("pod.create")]
    public async Task<IActionResult> VerifyPodOtp(Guid id, Guid waypointId, [FromBody] VerifyOtpDto dto)
    {
        var guard = await GuardTripAsync(id);
        if (guard != null) return guard;

        var result = await _podService.VerifyOtpAsync(id, waypointId, User.GetUserIdString(), dto.Code, dto.Latitude, dto.Longitude);
        if (!result.Ok)
            return BadRequest(new ApiResponse<object>
            {
                Success = false,
                Message = result.Error,
                Data = new { result.OtpAttemptsRemaining }
            });
        var name = await _db.TripWaypoints.AsNoTracking()
            .Where(w => w.Id == waypointId).Select(w => w.Name).FirstOrDefaultAsync() ?? waypointId.ToString();
        return Ok(new ApiResponse<ProofOfDeliveryDto>
        {
            Success = true,
            Message = "OTP verified — proof of delivery recorded.",
            Data = ToPodDto(result.Record!, name)
        });
    }

    private static ProofOfDeliveryDto ToPodDto(ProofOfDelivery p, string waypointName) => new()
    {
        Id = p.Id,
        TripId = p.TripId,
        WaypointId = p.WaypointId,
        WaypointName = waypointName,
        Type = (int)p.Type,
        TypeName = p.Type.ToString(),
        SignatureSvg = p.SignatureSvg,
        ImageUrl = p.ImageUrl,
        OtpVerified = p.OtpVerifiedAt.HasValue,
        OtpVerifiedAt = p.OtpVerifiedAt,
        OtpAttemptsRemaining = Math.Max(0, ProofOfDeliveryService.MaxOtpAttempts - p.OtpFailedAttempts),
        Verified = p.IsVerified,
        CapturedBy = p.CapturedBy,
        CapturedAt = p.CapturedAt,
        Latitude = p.Latitude,
        Longitude = p.Longitude,
        LocationMismatch = p.LocationMismatch,
        LocationMismatchDetail = p.LocationMismatchDetail,
        Notes = p.Notes
    };
}

/// <summary>Multipart photo capture: the image file plus optional capture geolocation and notes.</summary>
public class UploadPodPhotoDto
{
    public IFormFile? File { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Notes { get; set; }
}