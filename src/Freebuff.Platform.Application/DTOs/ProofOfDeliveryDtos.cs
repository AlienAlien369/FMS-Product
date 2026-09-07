using System.ComponentModel.DataAnnotations;

namespace Freebuff.Platform.Application.DTOs;

public class ProofOfDeliveryDto
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public Guid WaypointId { get; set; }
    public string WaypointName { get; set; } = string.Empty;
    public int Type { get; set; }
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Signature evidence (SVG markup) — present when Type = signature.</summary>
    public string? SignatureSvg { get; set; }

    /// <summary>Photo evidence reference — present when Type = photo.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>True once the OTP code matched (Type = otp_code).</summary>
    public bool OtpVerified { get; set; }
    public DateTime? OtpVerifiedAt { get; set; }

    /// <summary>Whether the evidence is complete for its type (signature/photo captured, OTP verified).</summary>
    public bool Verified { get; set; }

    public string CapturedBy { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>Data-quality/fraud flag — capture location far from the expected delivery geofence.</summary>
    public bool? LocationMismatch { get; set; }
    public string? LocationMismatchDetail { get; set; }
    public string? Notes { get; set; }
}

public class CreatePodSignatureDto
{
    [Required]
    public string SignatureSvg { get; set; } = string.Empty;

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Notes { get; set; }
}

public class CreatePodPhotoDto
{
    [Required]
    public string ImageUrl { get; set; } = string.Empty;

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Notes { get; set; }
}

public class VerifyOtpDto
{
    [Required, StringLength(6, MinimumLength = 6)]
    public string Code { get; set; } = string.Empty;

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}