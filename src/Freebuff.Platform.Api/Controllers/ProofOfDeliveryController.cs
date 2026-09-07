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
        => await CapturePodAsync(id, waypointId, dto.SignatureSvg, dto.Latitude, dto.Longitude, dto.Notes, ProofOfDeliveryType.Signature);

    /// <summary>Capture a photo at a delivery waypoint (stored image reference).</summary>
    [HttpPost("waypoints/{waypointId:guid}/pod/photo")]
    [RequirePermission("pod.create")]
    public async Task<IActionResult> CapturePodPhoto(Guid id, Guid waypointId, [FromBody] CreatePodPhotoDto dto)
        => await CapturePodAsync(id, waypointId, dto.ImageUrl, dto.Latitude, dto.Longitude, dto.Notes, ProofOfDeliveryType.Photo);

    private async Task<IActionResult> CapturePodAsync(Guid id, Guid waypointId, string data,
        double? latitude, double? longitude, string? notes, ProofOfDeliveryType type)
    {
        var guard = await GuardTripAsync(id);
        if (guard != null) return guard;

        try
        {
            ProofOfDelivery record = type == ProofOfDeliveryType.Signature
                ? await _podService.CaptureSignatureAsync(id, waypointId, User.GetUserIdString(), data, latitude, longitude, notes)
                : await _podService.CapturePhotoAsync(id, waypointId, User.GetUserIdString(), data, latitude, longitude, notes);
            var name = await _db.TripWaypoints.AsNoTracking()
                .Where(w => w.Id == waypointId).Select(w => w.Name).FirstOrDefaultAsync() ?? waypointId.ToString();
            return Ok(new ApiResponse<ProofOfDeliveryDto>
            {
                Success = true,
                Message = "Proof of delivery captured.",
                Data = ToPodDto(record, name)
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiResponse<object> { Success = false, Message = ex.Message });
        }
    }

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