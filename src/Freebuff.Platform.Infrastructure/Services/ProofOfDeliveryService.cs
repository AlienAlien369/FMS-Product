using System.Security.Cryptography;
using System.Text;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Geofencing;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>Outcome of an OTP verification attempt.</summary>
public sealed record OtpVerifyResult(bool Ok, string? Error, ProofOfDelivery? Record, int OtpAttemptsRemaining = 0);

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

    /// <summary>Max wrong OTP guesses before the code is locked — a 6-digit code
    /// must not be brute-forceable through the verify endpoint.</summary>
    public const int MaxOtpAttempts = 5;

    /// <summary>Upload cap for POD photos — a delivery snapshot, not a video.</summary>
    public const long MaxPhotoBytes = 5 * 1024 * 1024; // 5 MB

    private readonly ApplicationDbContext _db;
    private readonly string _uploadsPath;

    public ProofOfDeliveryService(ApplicationDbContext db, string uploadsPath = "uploads")
    {
        _db = db;
        _uploadsPath = uploadsPath;
    }

    // ── Photo upload: validation + storage (stored file reference, not base64) ──

    /// <summary>Content types accepted for POD photos (and the extensions they map to).</summary>
    private static readonly Dictionary<string, string> PhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif",
        ["image/heic"] = ".heic",
        ["image/heif"] = ".heif",
        ["image/bmp"] = ".bmp"
    };

    /// <summary>
    /// Validates a photo upload: content-type must be a known image type and the
    /// size within the 5 MB cap. Kept static so the gates are testable without a
    /// request/stream; the controller applies them before anything touches disk.
    /// </summary>
    public static (bool Ok, string? Error) ValidatePhotoUpload(string? contentType, long length)
    {
        if (string.IsNullOrWhiteSpace(contentType) || !PhotoExtensions.ContainsKey(contentType))
            return (false, "Only image uploads are allowed (JPEG, PNG, WebP, GIF, HEIC, BMP).");
        if (length <= 0)
            return (false, "The uploaded photo is empty.");
        if (length > MaxPhotoBytes)
            return (false, $"Photo exceeds the {MaxPhotoBytes / (1024 * 1024)} MB upload limit.");
        return (true, null);
    }

    /// <summary>
    /// Writes an uploaded photo to uploads/{tripId}/ (config-driven root, default
    /// "uploads") under a server-generated GUID name — the client's filename is
    /// never trusted — and returns the served URL reference stored on the record.
    /// </summary>
    public async Task<string> StorePhotoAsync(Guid tripId, string contentType, Stream content)
    {
        var tripDir = Path.Combine(_uploadsPath, tripId.ToString());
        Directory.CreateDirectory(tripDir);
        var fileName = Guid.NewGuid().ToString("N") + PhotoExtensions[contentType];
        var fullPath = Path.Combine(tripDir, fileName);
        await using (var file = File.Create(fullPath))
        {
            await content.CopyToAsync(file);
        }
        return $"/api/v1/trips/{tripId}/pod/photos/{fileName}";
    }

    /// <summary>
    /// Resolves a stored photo fileName to its full path for serving. Returns null
    /// for anything that could escape uploads/{tripId}/ (separators, dots, invalid
    /// chars) or that doesn't exist — the URL in ImageUrl is server-generated, but
    /// the serve endpoint must not trust it blindly.
    /// </summary>
    public (string? FullPath, string? ContentType) ResolvePhoto(Guid tripId, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.StartsWith('.')
            || fileName != Path.GetFileName(fileName) // any directory separator
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return (null, null);

        var tripDir = Path.GetFullPath(Path.Combine(_uploadsPath, tripId.ToString()));
        var fullPath = Path.GetFullPath(Path.Combine(tripDir, fileName));
        if (!fullPath.StartsWith(tripDir + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return (null, null); // belt-and-braces containment check
        if (!File.Exists(fullPath)) return (null, null);

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var contentType = PhotoExtensions.FirstOrDefault(kv => kv.Value == ext).Key
            ?? "application/octet-stream";
        return (fullPath, contentType);
    }

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
            SignatureSvg = SanitizeSignatureSvg(signatureSvg),
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
            CapturedAt = DateTime.UtcNow,
            OtpFailedAttempts = 0 // a fresh code starts with the full guess budget
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
            return new OtpVerifyResult(false, "No OTP has been issued for this waypoint — send one first.", null, MaxOtpAttempts);

        var attemptsRemaining = MaxOtpAttempts - pending.OtpFailedAttempts;

        // Locked: exhausted the guess budget — even the correct code is refused
        // until a fresh OTP is issued (re-issue replaces the record).
        if (pending.OtpFailedAttempts >= MaxOtpAttempts)
            return new OtpVerifyResult(false,
                $"This OTP is locked after {MaxOtpAttempts} failed attempts — request a new one.", pending, 0);
        if (DateTime.UtcNow - pending.CapturedAt > OtpLifetime)
            return new OtpVerifyResult(false, "This OTP has expired — request a new one.", pending, attemptsRemaining);

        if (!string.Equals(pending.OtpCodeHash, HashCode(code), StringComparison.Ordinal))
        {
            pending.OtpFailedAttempts++;
            await _db.SaveChangesAsync();
            return new OtpVerifyResult(false, "Incorrect OTP code.", pending,
                Math.Max(0, MaxOtpAttempts - pending.OtpFailedAttempts));
        }

        pending.OtpVerifiedAt = DateTime.UtcNow;
        pending.CapturedBy = capturedBy;
        var (mismatch, detail) = await EvaluateLocationMismatchAsync(waypointId, latitude, longitude);
        pending.Latitude = latitude;
        pending.Longitude = longitude;
        pending.LocationMismatch = mismatch;
        pending.LocationMismatchDetail = detail;
        await _db.SaveChangesAsync();
        return new OtpVerifyResult(true, null, pending, attemptsRemaining);
    }

    // ── Internals ──────────────────────────────────────────────────────────

    /// <summary>
    /// Allowlist-based sanitization of captured signature SVG so user-controlled
    /// markup can never reach the DOM as active content. Only the minimal
    /// self-contained drawing surface survives: the svg root, path elements, and
    /// the stroke/fill/viewBox/d geometry attributes the signature pad emits.
    /// Everything else — script, foreignObject, on* event handlers, style — is
    /// stripped. Applied at capture time (defense in depth; the viewer also
    /// escapes on render).
    /// </summary>
    internal static string SanitizeSignatureSvg(string svg)
    {
        if (string.IsNullOrWhiteSpace(svg)) return svg;

        var allowedAttrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "d", "stroke", "stroke-width", "stroke-linecap", "stroke-linejoin", "stroke-miterlimit",
            "fill", "fill-rule", "fill-opacity", "stroke-opacity", "viewbox", "xmlns", "width", "height",
            "x", "y"
        };
        var allowedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "svg", "path", "g", "line", "polyline", "circle", "rect" };

        var result = new StringBuilder(svg.Length);
        var i = 0;
        var n = svg.Length;
        while (i < n)
        {
            var lt = svg.IndexOf('<', i);
            if (lt < 0) { result.Append(svg, i, n - i); break; }
            result.Append(svg, i, lt - i); // text before the tag (e.g. whitespace)
            var gt = svg.IndexOf('>', lt);
            if (gt < 0) { result.Append(svg, lt, n - lt); break; } // unterminated tag — drop the rest safely

            var tagText = svg.Substring(lt, gt - lt + 1);
            result.Append(SanitizeTag(tagText, allowedTags, allowedAttrs));
            i = gt + 1;
        }
        return result.ToString();
    }

    private static string SanitizeTag(string tag, HashSet<string> allowedTags, HashSet<string> allowedAttrs)
    {
        var inner = tag.Substring(1, tag.Length - 2).Trim();
        if (inner.Length == 0) return string.Empty;
        var closing = inner.StartsWith("/");
        var selfClosing = !closing && inner.EndsWith("/");
        var trimmed = selfClosing ? inner.Substring(0, inner.Length - 1).TrimEnd() : inner;
        var tagNameEnd = closing ? trimmed.IndexOfAny(new[] { ' ', '\t', '\n', '>' }) : trimmed.IndexOfAny(new[] { ' ', '\t', '\n' });
        var name = (tagNameEnd < 0 ? trimmed : trimmed.Substring(0, tagNameEnd));
        if (name.StartsWith("/")) name = name.Substring(1);
        name = name.Trim();
        if (!allowedTags.Contains(name)) return string.Empty; // drop disallowed elements entirely

        // Rebuild a clean tag from allowed attributes only.
        var builder = new StringBuilder();
        if (closing)
        {
            builder.Append('<').Append('/').Append(name).Append('>');
        }
        else
        {
            builder.Append('<').Append(name);
            var attrStart = tagNameEnd < 0 ? trimmed.Length : tagNameEnd;
            foreach (var attr in ParseAttrs(trimmed.Substring(attrStart)))
            {
                if (!allowedAttrs.Contains(attr.Name)) continue;       // on* , href, style?, etc. stripped
                if (attr.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)) continue;
                builder.Append(' ').Append(attr.Name).Append('=').Append('\"').Append(attr.Value).Append('\"');
            }
            builder.Append(selfClosing ? "/>" : ">");
        }
        return builder.ToString();
    }

    private static IEnumerable<(string Name, string Value)> ParseAttrs(string segment)
    {
        var i = 0;
        var n = segment.Length;
        while (i < n)
        {
            // Skip whitespace
            while (i < n && (segment[i] == ' ' || segment[i] == '\t' || segment[i] == '\n')) i++;
            if (i >= n) yield break;
            var nameStart = i;
            while (i < n && segment[i] != '=' && segment[i] != ' ' && segment[i] != '\t' && segment[i] != '\n') i++;
            var name = segment.Substring(nameStart, i - nameStart);
            while (i < n && (segment[i] == ' ' || segment[i] == '\t')) i++;
            if (i >= n || segment[i] != '=')
            {
                if (name.Length > 0) yield return (name, string.Empty);
                continue;
            }
            i++; // skip '='
            while (i < n && (segment[i] == ' ' || segment[i] == '\t')) i++;
            if (i >= n) { yield return (name, string.Empty); break; }
            var quote = segment[i] == '\"' || segment[i] == '\'';
            string value;
            if (quote)
            {
                var q = segment[i];
                i++;
                var vs = i;
                while (i < n && segment[i] != q) i++;
                value = segment.Substring(vs, i - vs);
                if (i < n) i++; // skip closing quote
            }
            else
            {
                var vs = i;
                while (i < n && segment[i] != ' ' && segment[i] != '\t' && segment[i] != '\n') i++;
                value = segment.Substring(vs, i - vs);
            }
            yield return (name, value);
        }
    }

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