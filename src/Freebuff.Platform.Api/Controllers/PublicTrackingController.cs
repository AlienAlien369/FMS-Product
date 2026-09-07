using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Controllers;

/// <summary>
/// The public Customer Tracking Link surface — the first endpoints exposed to
/// someone who is not a platform user. Access is validated by possession of an
/// unguessable share token alone: no login, no role, no company membership, and
/// deliberately NOT routed through the RBAC/permission registry (a token must
/// never inherit broader access via a role mapping).
///
/// The payload is the entire contract (a viewer can inspect network requests):
/// nothing beyond PublicTripTrackingDto is ever serialized. Rate limited per
/// token / per IP so the unauthenticated surface can't be scraped or
/// brute-forced; a dead (unknown/expired/revoked) token yields ONE clean state.
/// </summary>
[ApiController]
[Route("api/v1/public/trips")]
public class PublicTrackingController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly TripShareLinkService _links;
    private readonly ProofOfDeliveryService _pod;

    public PublicTrackingController(ApplicationDbContext db, TripShareLinkService links, ProofOfDeliveryService pod)
    {
        _db = db;
        _links = links;
        _pod = pod;
    }

    /// <summary>Read-only tracking view for one trip, keyed by the share token.</summary>
    [HttpGet("{token}")]
    public async Task<IActionResult> Get(string token)
    {
        var resolved = await _links.ResolveValidAsync(token);
        if (resolved == null) return DeadLink();

        var (link, trip) = resolved.Value;
        var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == trip.CompanyId);

        var waypoints = await _db.TripWaypoints.AsNoTracking()
            .Where(w => w.TripId == trip.Id && !w.IsDeleted)
            .OrderBy(w => w.SequenceOrder)
            .ToListAsync();

        var waypointDtos = new List<PublicWaypointDto>();
        foreach (var wp in waypoints)
        {
            var evidence = await _pod.ListForWaypointAsync(trip.Id, wp.Id);
            waypointDtos.Add(new PublicWaypointDto
            {
                Name = wp.Name,
                SequenceOrder = wp.SequenceOrder,
                Latitude = wp.Latitude,
                Longitude = wp.Longitude,
                Arrived = wp.ActualArrival.HasValue,
                ExpectedArrivalUtc = wp.ExpectedArrival,
                PodEvidence = evidence.Select(p => ToPublicPod(p, link.Token)).ToList()
            });
        }

        // Live vehicle position (in-progress) from the normalized telemetry state.
        PublicVehiclePositionDto? position = null;
        var state = await _db.TelemetryStates.AsNoTracking()
            .FirstOrDefaultAsync(s => s.VehicleId == trip.VehicleId);
        if (state != null)
        {
            position = new PublicVehiclePositionDto
            {
                Latitude = state.Latitude,
                Longitude = state.Longitude,
                SpeedKmh = state.SpeedKmh,
                HeadingDeg = state.HeadingDeg,
                UpdatedAt = state.UpdatedAt
            };
        }

        // ETA: reuse the trip module's expected-arrival data — live estimate to
        // the next un-arrived stop from position/speed, plus the scheduled final
        // arrival for the shipment.
        int? etaMinutes = null;
        var nextStop = waypoints.FirstOrDefault(w => !w.ActualArrival.HasValue);
        if (nextStop != null && state?.Latitude.HasValue == true && state.Longitude.HasValue)
        {
            var km = HaversineKm(state.Latitude.Value, state.Longitude.Value, nextStop.Latitude, nextStop.Longitude);
            var speed = Math.Max(state.SpeedKmh ?? 0, 15.0); // floor: assume at least walking pace of progress
            etaMinutes = speed > 0 ? (int)Math.Ceiling(km / speed * 60) : null;
        }
        var finalExpected = waypoints.LastOrDefault()?.ExpectedArrival ?? trip.ScheduledEndTime;

        return Ok(new ApiResponse<PublicTripTrackingDto>
        {
            Success = true,
            Data = new PublicTripTrackingDto
            {
                Status = (int)trip.Status,
                StatusLabel = FriendlyStatus(trip),
                IsDelayed = trip.IsDelayed,
                TripName = trip.Name,
                CompanyName = company?.Name ?? string.Empty,
                VehiclePosition = position,
                Waypoints = waypointDtos,
                EtaMinutesToNextStop = etaMinutes,
                ExpectedFinalArrivalUtc = finalExpected,
                RoutePath = waypoints.Select(w => new[] { w.Latitude, w.Longitude }).ToList()
            }
        });
    }

    /// <summary>
    /// Serves a stored POD photo to the public viewer through the token-scoped
    /// route (photo URLs in the tracking payload point here). No auth beyond the
    /// token; the file is resolved under the trip's uploads directory.
    /// </summary>
    [HttpGet("{token}/photos/{fileName}")]
    public async Task<IActionResult> GetPhoto(string token, string fileName)
    {
        var resolved = await _links.ResolveValidAsync(token);
        if (resolved == null) return DeadLink();

        var (fullPath, contentType) = _pod.ResolvePhoto(resolved.Value.Link.TripId, fileName);
        if (fullPath == null) return DeadLink();
        return PhysicalFile(fullPath, contentType ?? "application/octet-stream", enableRangeProcessing: true);
    }

    private static IActionResult DeadLink()
        => new NotFoundObjectResult(new ApiResponse<object>
        {
            Success = false,
            Message = "This tracking link is no longer available."
        });

    /// <summary>Customer-friendly status language — never internal enum names.</summary>
    private static string FriendlyStatus(Trip t) => t.Status switch
    {
        TripStatus.Draft => "Preparing",
        TripStatus.Scheduled => "Scheduled",
        TripStatus.InProgress => t.IsDelayed ? "Delayed" : "In Transit",
        TripStatus.Completed => "Delivered",
        TripStatus.Cancelled => "Cancelled",
        _ => t.Status.ToString()
    };

    /// <summary>
    /// Public POD mapping. Photo references stored as /api/v1/trips/... internal
    /// URLs are rewritten to the public token-scoped route; legacy base64 data
    /// URLs pass through. CapturedBy / Notes / internal IDs are never mapped.
    /// </summary>
    private static PublicPodEvidenceDto ToPublicPod(ProofOfDelivery p, string token)
    {
        var imageUrl = p.ImageUrl;
        if (imageUrl != null && imageUrl.StartsWith("/api/v1/trips/", StringComparison.Ordinal))
        {
            var fileName = imageUrl.Substring(imageUrl.LastIndexOf('/') + 1);
            imageUrl = $"/api/v1/public/trips/{token}/photos/{fileName}";
        }
        return new PublicPodEvidenceDto
        {
            Type = (int)p.Type,
            TypeName = p.Type.ToString(),
            Verified = p.IsVerified,
            OtpVerifiedAt = p.OtpVerifiedAt,
            CapturedAt = p.CapturedAt,
            Latitude = p.Latitude,
            Longitude = p.Longitude,
            LocationMismatch = p.LocationMismatch,
            SignatureSvg = p.SignatureSvg,
            ImageUrl = imageUrl
        };
    }

    private static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6371.0;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLng = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
            * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}