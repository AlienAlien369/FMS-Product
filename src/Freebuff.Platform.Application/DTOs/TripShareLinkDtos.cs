namespace Freebuff.Platform.Application.DTOs;

/// <summary>Internal (authenticated) share-link management surface.</summary>
public class TripShareLinkDto
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public string Token { get; set; } = string.Empty;

    /// <summary>Relative SPA route the customer opens — the shareable link.</summary>
    public string ShareUrl { get; set; } = string.Empty;

    public DateTime? ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class CreateTripShareLinkDto
{
    /// <summary>Optional explicit lifetime in days; null = trip completion + default days.</summary>
    public int? ExpiresInDays { get; set; }
}

/// <summary>
/// The ENTIRE public tracking payload — deliberately minimal. A determined
/// viewer can inspect network requests, so this DTO is the contract: nothing
/// outside it is ever serialized. Excludes driver identity, vehicle
/// registration/internal IDs, customer contact info, geofence/route
/// configuration, alerts/safety events, and pricing — this is what a customer
/// needs to know about their shipment, not an ops window.
/// </summary>
public class PublicTripTrackingDto
{
    public int Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public bool IsDelayed { get; set; }
    public string TripName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;

    public PublicVehiclePositionDto? VehiclePosition { get; set; }

    public List<PublicWaypointDto> Waypoints { get; set; } = new();

    /// <summary>Live estimate to the next un-arrived stop (minutes), when position + speed exist.</summary>
    public int? EtaMinutesToNextStop { get; set; }

    public DateTime? ExpectedFinalArrivalUtc { get; set; }

    /// <summary>The shipment's path as [lat, lng] pairs (from waypoints) for the map.</summary>
    public List<double[]> RoutePath { get; set; } = new();
}

public class PublicVehiclePositionDto
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? SpeedKmh { get; set; }
    public double? HeadingDeg { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class PublicWaypointDto
{
    public string Name { get; set; } = string.Empty;
    public int SequenceOrder { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public bool Arrived { get; set; }
    public DateTime? ExpectedArrivalUtc { get; set; }
    public List<PublicPodEvidenceDto> PodEvidence { get; set; } = new();
}

/// <summary>
/// POD evidence as the customer may see it: type + verification state + the
/// signature SVG / photo. Photo URLs are rewritten to the public, token-scoped
/// route. Captured-by, notes and waypoint/trip internal IDs are deliberately
/// absent.
/// </summary>
public class PublicPodEvidenceDto
{
    public int Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public bool Verified { get; set; }
    public DateTime? OtpVerifiedAt { get; set; }
    public DateTime CapturedAt { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool? LocationMismatch { get; set; }
    public string? SignatureSvg { get; set; }
    public string? ImageUrl { get; set; }
}