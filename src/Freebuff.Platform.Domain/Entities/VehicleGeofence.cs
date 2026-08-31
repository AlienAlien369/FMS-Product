using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Junction: Vehicle/Driver ↔ Geofence with entry/exit alert configuration
/// </summary>
public class VehicleGeofence : BaseEntity
{
    public Guid VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;

    public Guid? DriverId { get; set; }
    public Driver? Driver { get; set; }

    public Guid GeofenceId { get; set; }
    public Geofence Geofence { get; set; } = null!;

    public bool AlertOnEntry { get; set; } = true;
    public bool AlertOnExit { get; set; } = true;
    public bool AlertOnDwell { get; set; }
    public int? DwellTimeMinutes { get; set; }
}
