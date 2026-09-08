using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Per-vehicle maintenance schedule — the predictive half of the Maintenance
/// module. A schedule describes ONE recurring service (oil change, tyre
/// rotation, …) driven by a trigger (mileage / time / engine hours) and an
/// interval. The "last serviced" refs are updated when a MaintenanceRecord is
/// completed against the schedule, which rolls the NextDue* point forward.
/// </summary>
public class MaintenanceSchedule : BaseEntity
{
    public Guid VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;
    public Guid CompanyId { get; set; }

    /// <summary>Short display name, e.g. "Engine oil change".</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Canonical service kind: oil_change | tire_rotation | brake_inspection | general_inspection | custom.</summary>
    public string ServiceType { get; set; } = "general_inspection";

    public MaintenanceTriggerType TriggerType { get; set; } = MaintenanceTriggerType.MileageInterval;

    /// <summary>Interval magnitude in the trigger's unit (km / days / engine hours).</summary>
    public decimal IntervalValue { get; set; }

    /// <summary>
    /// Lead window before the due point that flips the schedule to "due soon"
    /// (same unit as the trigger: km / days / engine hours). Null → no lead
    /// window; the schedule goes straight from ok to overdue.
    /// </summary>
    public decimal? DueLeadValue { get; set; }

    // ── Last-serviced refs (rolled forward when a record completes) ────────
    public decimal? LastServiceOdometer { get; set; }
    public decimal? LastServiceEngineHours { get; set; }
    public DateTime? LastServiceDate { get; set; }
    public Guid? LastMaintenanceRecordId { get; set; }

    // ── Next due point (maintained on log-service + odometer/hour advance) ─
    public decimal? NextDueOdometer { get; set; }
    public decimal? NextDueEngineHours { get; set; }
    public DateTime? NextDueDate { get; set; }

    public bool IsActive { get; set; } = true;
}