using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Trip entity with full lifecycle support including round trips.
/// </summary>
public class Trip : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TripStatus Status { get; set; } = TripStatus.Planned;
    public bool IsRoundTrip { get; set; }

    // Locations
    public string StartLocation { get; set; } = string.Empty;
    public double StartLatitude { get; set; }
    public double StartLongitude { get; set; }
    public string? EndLocation { get; set; }
    public double? EndLatitude { get; set; }
    public double? EndLongitude { get; set; }
    public string? ViaPoints { get; set; } // JSON array of waypoints
    public string? Stops { get; set; } // JSON array of stops

    // Scheduling
    public DateTime? ScheduledStartTime { get; set; }
    public DateTime? ScheduledEndTime { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }

    // Metrics
    public decimal? PlannedDistance { get; set; }
    public decimal? ActualDistance { get; set; }
    public TimeSpan? PlannedDuration { get; set; }
    public TimeSpan? ActualDuration { get; set; }
    public decimal? MaxSpeed { get; set; }
    public decimal? AverageSpeed { get; set; }

    // Associations
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;

    public Guid? DriverId { get; set; }
    public Driver? Driver { get; set; }

    public Guid? ClientId { get; set; }
    public Client? Client { get; set; }

    public string? RouteData { get; set; } // JSON route geometry
}
