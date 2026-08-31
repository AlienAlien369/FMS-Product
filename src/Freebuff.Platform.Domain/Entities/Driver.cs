using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Driver entity with full profile and behaviour tracking support.
/// </summary>
public class Driver : BaseEntity
{
    public string EmployeeId { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? LicenseNumber { get; set; }
    public DateTime? LicenseExpiry { get; set; }
    public string? LicenseCategory { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }

    // Company
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    // Status
    public DriverStatus Status { get; set; } = DriverStatus.Active;

    // Scores
    public decimal? SafetyScore { get; set; }
    public decimal? BehaviourScore { get; set; }

    // Metadata
    public string? ProfileImageUrl { get; set; }
    public string? CustomAttributes { get; set; } // JSON

    // Navigation
    public ICollection<Vehicle> AssignedVehicles { get; set; } = new List<Vehicle>();
    public ICollection<Trip> Trips { get; set; } = new List<Trip>();
    public ICollection<VehicleGeofence> DriverGeofences { get; set; } = new List<VehicleGeofence>();

    public string FullName => $"{FirstName} {LastName}".Trim();
}
