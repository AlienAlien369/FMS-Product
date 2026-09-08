using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Maintenance record — one actual service event for the Maintenance module.
///
/// Two flavors, deliberately distinguished because they tell different
/// operational stories (a fleet with a rising breakdown-to-preventive ratio
/// has a real problem):
///   - Preventive:  scheduled service completed against a MaintenanceSchedule
///     (rolls the schedule's last-serviced refs + next due forward).
///   - Breakdown:   unscheduled failure with severity, downtime and root cause.
///
/// Legacy MaintenanceType ("preventive"/"corrective") is kept for back-compat;
/// new rows write the canonical RecordType enum.
/// </summary>
public class MaintenanceRecord : BaseEntity
{
    public Guid VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;
    public Guid CompanyId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string MaintenanceType { get; set; } = "preventive"; // legacy: preventive, corrective
    public MaintenanceRecordType RecordType { get; set; } = MaintenanceRecordType.Preventive;

    /// <summary>The schedule this record completes, when it is a preventive service.</summary>
    public Guid? MaintenanceScheduleId { get; set; }
    public MaintenanceSchedule? MaintenanceSchedule { get; set; }

    /// <summary>Breakdown severity: minor | major | critical (null for preventive records).</summary>
    public string? BreakdownSeverity { get; set; }

    /// <summary>Vehicle downtime caused by a breakdown, in hours.</summary>
    public decimal? DowntimeHours { get; set; }

    /// <summary>Root cause of a breakdown, when known.</summary>
    public string? RootCause { get; set; }

    public string? Workshop { get; set; }
    public decimal? Cost { get; set; }
    public string? Currency { get; set; } = "USD";
    public decimal? OdometerAtService { get; set; }
    public decimal? EngineHoursAtService { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public bool IsCompleted { get; set; }
    public string? PartsReplaced { get; set; } // JSON
    public string? Notes { get; set; }
}
