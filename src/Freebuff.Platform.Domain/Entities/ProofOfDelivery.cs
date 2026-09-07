using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Proof-of-delivery evidence captured at a specific trip waypoint. One record
/// per capture event — a waypoint may accumulate several (e.g. signature +
/// photo), and OTP captures are a two-phase lifecycle (generated → verified).
///
/// Tenant isolation mirrors the parent Trip (TenantId = CompanyId). Geolocation
/// at capture is cross-referenced against the waypoint's expected location (its
/// linked geofence, else its own coordinates ± buffer) and flagged as
/// LocationMismatch — a data-quality/fraud signal, never a hard block.
///
/// Captured data is stored per type:
///   Signature → SignatureSvg (SVG markup of the pen strokes)
///   Photo     → ImageUrl (stored image reference; data URL in dev, object-storage URL in production)
///   OTP       → OtpCodeHash (SHA-256 of the code) + OtpVerifiedAt when the customer's code matched
/// </summary>
public class ProofOfDelivery : BaseEntity
{
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;

    /// <summary>The trip waypoint this delivery was captured at.</summary>
    public Guid WaypointId { get; set; }
    public TripWaypoint Waypoint { get; set; } = null!;

    public Guid CompanyId { get; set; }

    public ProofOfDeliveryType Type { get; set; }

    // Signature
    public string? SignatureSvg { get; set; }

    // Photo — stored reference (data URL in dev; object-storage URL in production)
    public string? ImageUrl { get; set; }

    // OTP — SHA-256 hash of the code; never the plaintext code
    public string? OtpCodeHash { get; set; }
    public DateTime? OtpVerifiedAt { get; set; }

    /// <summary>Who/which device captured the evidence (user id, or device id for a driver app).</summary>
    public string CapturedBy { get; set; } = string.Empty;

    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    // Capture geolocation + data-quality flag
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool? LocationMismatch { get; set; }
    public string? LocationMismatchDetail { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// The single definition of "evidence complete for its type" (signature/photo
    /// captured, OTP verified). Shared by the entity property, the capture service
    /// and the waypoint-arrival gate so the rule can never drift across layers.
    /// </summary>
    public static readonly System.Linq.Expressions.Expression<Func<ProofOfDelivery, bool>> HasVerifiedEvidenceExpr =
        p => (p.Type != ProofOfDeliveryType.OtpCode && (!string.IsNullOrEmpty(p.SignatureSvg) || !string.IsNullOrEmpty(p.ImageUrl)))
            || (p.Type == ProofOfDeliveryType.OtpCode && p.OtpVerifiedAt.HasValue);

    private static readonly Func<ProofOfDelivery, bool> _isVerified = HasVerifiedEvidenceExpr.Compile();

    /// <summary>True when the evidence is complete for its type (signature/photo captured, OTP verified).</summary>
    public bool IsVerified => _isVerified(this);
}