using System.Security.Cryptography;
using System.Text;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Geofencing;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>Outcome of an OTP verification attempt.</summary>
public sealed record OtpVerifyResult(bool Ok, string? Error, ProofOfDelivery? Record);

/// <summary>
/// Proof-of-delivery capture rules. One active record per (waypoint, type) —
/// re-capture replaces the previous evidence for that type (soft-delete +
/// insert) so the unique active index holds. Geolocation at capture is
/// cross-referenced against the waypoint's expected location (linked geofence,
/// else waypoint coordinates ± 500 m) and flagged as a data-quality/fraud
/// signal — never a hard block. Tenant isolation is the controller's job
/// (ownership checked before any call here); this service only assumes the
/// waypoint belongs to the given trip.
///
/// OTP is a two-phase lifecycle: SendOtpAsync stores ONLY a SHA-256 hash of the
/// code (never the plaintext) and returns the code for delivery (SMS/email
/// gateway is a deployment integration — dev returns it in the response);
/// VerifyOtpAsync matches a presented code, stamps OtpVerifiedAt and records
/// the capture geolocation. OTPs expire 10 minutes after generation.
/// </summary>
public class ProofOfDeliveryService
{
    private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(10);

    private readonly ApplicationDbContext _db;

    public ProofOfDeliveryService(ApplicationDbContext db) => _db = db;

    public Task<List<ProofOfDelivery>> ListForWaypointAsync(Guid tripId, Guid waypointId)
        => _db.ProofOfDeliveries.AsNoTracking()
            .Where(p => p.TripId == tripId && p.WaypointId == waypointId && !p.IsDeleted)
            .OrderByDescending(p => p.CapturedAt)
            .ToListAsync();

    public Task<List<ProofOfDelivery>> ListForTripAsync(Guid tripId)
        => _db.ProofOfDeliveries.AsNoTracking()
            .Where(p => p.TripId == tripId && !p.IsDeleted)
            .OrderByDescending(p => p.CapturedAt)
            .ToListAsync();

    /// <summary>True when verified POD evidence exists for the waypoint (signature/photo captured or OTP verified).</summary>
    public Task<bool> HasVerifiedEvidenceAsync(Guid tripId, Guid waypointId)
        => _db.ProofOfDeliveries.AsNoTracking()
            .Where(p => p.TripId == tripId && p.WaypointId == waypointId && !p.IsDeleted)
            .AnyAsync(ProofOfDelivery.HasVerifiedEvidenceExpr);

    public async Task<ProofOfDelivery> CaptureSignatureAsync(Guid tripId, Guid waypointId, string capturedBy,
        string signatureSvg, double? latitude = null, double? longitude = null, string? notes = null)
    {
        await EnsureWaypointOnTripAsync(tripId, waypointId);
        await ReplaceActiveAsync(waypointId, ProofOfDeliveryType.Signature, capturedBy);

        var (mismatch, detail) = await EvaluateLocationMismatchAsync(waypointId, latitude, longitude);
        var record = new ProofOfDelivery
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            WaypointId = waypointId,
            CompanyId = await GetCompanyIdAsync(tripId),
            Type = ProofOfDeliveryType.Signature,
            SignatureSvg = signatureSvg,
            CapturedBy = capturedBy,
            CapturedAt = DateTime.UtcNow,
            Latitude = latitude,
            Longitude = longitude,
            LocationMismatch = mismatch,
            LocationMismatchDetail = detail,
            Notes = notes
        };
        _db.ProofOfDeliveries.Add(record);
        await _db.SaveChangesAsync();
        return record;
    }

    public async Task<ProofOfDelivery> CapturePhotoAsync(Guid tripId, Guid waypointId, string capturedBy,
        string imageUrl, double? latitude = null, double? longitude = null, string? notes = null)
    {
        await EnsureWaypointOnTripAsync(tripId, waypointId);
        await ReplaceActiveAsync(waypointId, ProofOfDeliveryType.Photo, capturedBy);

        var (mismatch, detail) = await EvaluateLocationMismatchAsync(waypointId, latitude, longitude);
        var record = new ProofOfDelivery
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            WaypointId = waypointId,
            CompanyId = await GetCompanyIdAsync(tripId),
            Type = ProofOfDeliveryType.Photo,
            ImageUrl = imageUrl,
            CapturedBy = capturedBy,
            CapturedAt = DateTime.UtcNow,
            Latitude = latitude,
            Longitude = longitude,
            LocationMismatch = mismatch,
            LocationMismatchDetail = detail,
            Notes = notes
        };
        _db.ProofOfDeliveries.Add(record);
        await _db.SaveChangesAsync();
        return record;
    }

    /// <summary>Generates a 6-digit OTP, stores its SHA-256 hash, returns the plaintext code for delivery.</summary>
    public async Task<(ProofOfDelivery Record, string Otp)> SendOtpAsync(Guid tripId, Guid waypointId, string capturedBy)
    {
        await EnsureWaypointOnTripAsync(tripId, waypointId);
        await ReplaceActiveAsync(waypointId, ProofOfDeliveryType.OtpCode, capturedBy);

        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var record = new ProofOfDelivery
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            WaypointId = waypointId,
            CompanyId = await GetCompanyIdAsync(tripId),
            Type = ProofOfDeliveryType.OtpCode,
            OtpCodeHash = HashCode(otp),
            CapturedBy = capturedBy,
            CapturedAt = DateTime.UtcNow
        };
        _db.ProofOfDeliveries.Add(record);
        await _db.SaveChangesAsync();
        return (record, otp);
    }

    /// <summary>Matches a presented OTP against the active pending record, stamps verification + geolocation.</summary>
    public async Task<OtpVerifyResult> VerifyOtpAsync(Guid tripId, Guid waypointId, string capturedBy,
        string code, double? latitude, double? longitude)
    {
        var pending = await _db.ProofOfDeliveries
            .FirstOrDefaultAsync(p => p.TripId == tripId && p.WaypointId == waypointId
                && p.Type == ProofOfDeliveryType.OtpCode && !p.IsDeleted);
        if (pending == null || string.IsNullOrEmpty(pending.OtpCodeHash))
            return new OtpVerifyResult(false, "No OTP has been issued for this waypoint — send one first.", null);
        if (DateTime.UtcNow - pending.CapturedAt > OtpLifetime)
            return new OtpVerifyResult(false, "This OTP has expired — request a new one.", pending);

        if (!string.Equals(pending.OtpCodeHash, HashCode(code), StringComparison.Ordinal))
            return new OtpVerifyResult(false, "Incorrect OTP code.", pending);

        pending.OtpVerifiedAt = DateTime.UtcNow;
        pending.CapturedBy = capturedBy;
        var (mismatch, detail) = await EvaluateLocationMismatchAsync(waypointId, latitude, longitude);
        pending.Latitude = latitude;
        pending.Longitude = longitude;
        pending.LocationMismatch = mismatch;
        pending.LocationMismatchDetail = detail;
        await _db.SaveChangesAsync();
        return new OtpVerifyResult(true, null, pending);
    }

    // ── Internals ──────────────────────────────────────────────────────────

    private async Task EnsureWaypointOnTripAsync(Guid tripId, Guid waypointId)
    {
        var exists = await _db.TripWaypoints.AsNoTracking()
            .AnyAsync(w => w.Id == waypointId && w.TripId == tripId && !w.IsDeleted);
        if (!exists) throw new KeyNotFoundException("Waypoint not found on this trip.");
    }

    private async Task<Guid> GetCompanyIdAsync(Guid tripId)
        => await _db.Trips.AsNoTracking().Where(t => t.Id == tripId).Select(t => t.CompanyId).FirstOrDefaultAsync();

    /// <summary>Soft-delete the previous active record of the same type so the unique active index holds.</summary>
    private async Task ReplaceActiveAsync(Guid waypointId, ProofOfDeliveryType type, string replacedBy)
    {
        var existing = await _db.ProofOfDeliveries
            .FirstOrDefaultAsync(p => p.WaypointId == waypointId && p.Type == type && !p.IsDeleted);
        if (existing != null)
        {
            existing.IsDeleted = true;
            existing.DeletedAt = DateTime.UtcNow;
            existing.DeletedBy = $"pod:replace:{replacedBy}";
        }
    }

    /// <summary>
    /// Data-quality flag: capture geolocation vs the waypoint's expected location
    /// (linked geofence if present, else waypoint coordinates ± 500 m). Never a
    /// hard block — returns null (unknown) when no capture location is provided.
    /// </summary>
    private async Task<(bool? Mismatch, string? Detail)> EvaluateLocationMismatchAsync(Guid waypointId, double? latitude, double? longitude)
    {
        if (!latitude.HasValue || !longitude.HasValue)
            return (null, "no capture location provided");

        var wp = await _db.TripWaypoints.AsNoTracking().FirstOrDefaultAsync(w => w.Id == waypointId && !w.IsDeleted);
        if (wp == null) return (null, null);

        if (wp.LinkedGeofenceId.HasValue)
        {
            var fence = await _db.Geofences.AsNoTracking().FirstOrDefaultAsync(g => g.Id == wp.LinkedGeofenceId.Value && !g.IsDeleted);
            if (fence != null)
            {
                var inside = GeofenceContainment.IsInside(fence, latitude.Value, longitude.Value);
                return (!inside, inside
                    ? $"within expected geofence '{fence.Name}'"
                    : $"outside expected geofence '{fence.Name}'");
            }
        }

        var distanceM = HaversineMeters(wp.Latitude, wp.Longitude, latitude.Value, longitude.Value);
        var within = distanceM <= 500;
        return (!within, within
            ? $"within {distanceM:0}m of the waypoint"
            : $"captured {distanceM / 1000:0.0} km from the waypoint");
    }

    private static string HashCode(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes);
    }

    private static double HaversineMeters(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6371000.0;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLng = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
            * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}