namespace Freebuff.Platform.Application.DTOs;

public class RouteDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public int Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public bool IsOptimized { get; set; }
    public bool IsTemplate { get; set; }
    public string CompanyName { get; set; } = string.Empty;

    // Origin & Destination
    public string OriginName { get; set; } = string.Empty;
    public double OriginLatitude { get; set; }
    public double OriginLongitude { get; set; }
    public string? DestinationName { get; set; }
    public double? DestinationLatitude { get; set; }
    public double? DestinationLongitude { get; set; }

    // Waypoints & Geometry
    public string? Waypoints { get; set; }
    public string? RouteGeometry { get; set; }

    // Metrics
    public decimal? TotalDistance { get; set; }
    public string? DistanceUnit { get; set; }
    public TimeSpan? EstimatedDuration { get; set; }
    public decimal? EstimatedFuelCost { get; set; }
    public decimal? EstimatedTollCost { get; set; }
    public string? Currency { get; set; }
    public int? TrafficLevel { get; set; }

    // Constraints
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public int? MaxVehicles { get; set; }
    public int? Priority { get; set; }

    // Scheduling
    public string? RecurrenceRule { get; set; }
    public int? DayOfWeek { get; set; }
    public TimeSpan? PreferredStartTime { get; set; }

    // Assignments
    public int AssignedVehicleCount { get; set; }
    public int CompletedTripCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateRouteDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Type { get; set; }
    public bool IsTemplate { get; set; }

    // Origin & Destination
    public string OriginName { get; set; } = string.Empty;
    public double OriginLatitude { get; set; }
    public double OriginLongitude { get; set; }
    public string? DestinationName { get; set; }
    public double? DestinationLatitude { get; set; }
    public double? DestinationLongitude { get; set; }

    // Waypoints & Geometry
    public string? Waypoints { get; set; }
    public string? RouteGeometry { get; set; }

    // Metrics
    public decimal? TotalDistance { get; set; }
    public string? DistanceUnit { get; set; } = "km";
    public TimeSpan? EstimatedDuration { get; set; }
    public decimal? EstimatedFuelCost { get; set; }
    public decimal? EstimatedTollCost { get; set; }
    public string? Currency { get; set; } = "USD";
    public int? TrafficLevel { get; set; }

    // Constraints
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public int? MaxVehicles { get; set; }
    public int? Priority { get; set; }

    // Scheduling
    public string? RecurrenceRule { get; set; }
    public int? DayOfWeek { get; set; }
    public TimeSpan? PreferredStartTime { get; set; }
}

public class UpdateRouteDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? Status { get; set; }
    public int? Type { get; set; }
    public bool? IsOptimized { get; set; }
    public bool? IsTemplate { get; set; }

    // Origin & Destination
    public string? OriginName { get; set; }
    public double? OriginLatitude { get; set; }
    public double? OriginLongitude { get; set; }
    public string? DestinationName { get; set; }
    public double? DestinationLatitude { get; set; }
    public double? DestinationLongitude { get; set; }

    // Waypoints & Geometry
    public string? Waypoints { get; set; }
    public string? RouteGeometry { get; set; }

    // Metrics
    public decimal? TotalDistance { get; set; }
    public TimeSpan? EstimatedDuration { get; set; }
    public decimal? EstimatedFuelCost { get; set; }
    public decimal? EstimatedTollCost { get; set; }
    public int? TrafficLevel { get; set; }

    // Constraints
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public int? MaxVehicles { get; set; }
    public int? Priority { get; set; }

    // Scheduling
    public string? RecurrenceRule { get; set; }
    public int? DayOfWeek { get; set; }
    public TimeSpan? PreferredStartTime { get; set; }
}

public class GeofenceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int Status { get; set; }
    public string CompanyName { get; set; } = string.Empty;

    // Shape data
    public string Coordinates { get; set; } = string.Empty;
    public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; }
    public double? Radius { get; set; }

    // Style
    public string? FillColor { get; set; }
    public string? BorderColor { get; set; }
    public int? BorderWidth { get; set; }

    // Alerts
    public bool AlertOnEntry { get; set; } = true;
    public bool AlertOnExit { get; set; } = true;
    public bool AlertOnDwell { get; set; }
    public int? DwellTimeMinutes { get; set; }

    // Stats
    public int AssignedVehicleCount { get; set; }
    public int ViolationCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateGeofenceDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Type { get; set; }

    // Shape data
    public string Coordinates { get; set; } = string.Empty;
    public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; }
    public double? Radius { get; set; }

    // Style
    public string? FillColor { get; set; }
    public string? BorderColor { get; set; }
    public int? BorderWidth { get; set; }

    // Alerts
    public bool AlertOnEntry { get; set; } = true;
    public bool AlertOnExit { get; set; } = true;
    public bool AlertOnDwell { get; set; }
    public int? DwellTimeMinutes { get; set; }

    // Vehicle assignments
    public List<Guid>? AssignedVehicleIds { get; set; }
}

public class UpdateGeofenceDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? Status { get; set; }
    public int? Type { get; set; }

    // Shape data
    public string? Coordinates { get; set; }
    public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; }
    public double? Radius { get; set; }

    // Style
    public string? FillColor { get; set; }
    public string? BorderColor { get; set; }
    public int? BorderWidth { get; set; }

    // Alerts
    public bool? AlertOnEntry { get; set; }
    public bool? AlertOnExit { get; set; }
    public bool? AlertOnDwell { get; set; }
    public int? DwellTimeMinutes { get; set; }
}
