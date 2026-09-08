using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// A user-configured recurring report — the scheduled-report half of the
/// Reports module. When the cadence comes due, the Report Engine re-runs the
/// report with the stored parameters and delivers the output through the
/// Notification system (event type "report.scheduled_delivered") to the
/// creating user — no separate email stack; the notification delivery
/// infrastructure is reused as-is.
///
/// Parameters are the same ReportRequest the on-demand runner accepts,
/// serialized as JSON: { dateRange, vehicleIds, driverIds, groupBy }.
/// </summary>
public class ScheduledReport : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>User who configured the report — delivery target for the generated output.</summary>
    public Guid CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;

    /// <summary>Report-type code (ReportEngine registry code, e.g. "fuel").</summary>
    public string ReportType { get; set; } = string.Empty;

    /// <summary>Free-text display name, e.g. "Weekly fuel spend".</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>JSON-serialized report parameters (date range, vehicle/driver filters).</summary>
    public string ParametersJson { get; set; } = "{}";

    public ReportCadence Cadence { get; set; } = ReportCadence.Daily;

    /// <summary>0 (Sunday)..6 — required when Cadence = Weekly.</summary>
    public int? DayOfWeek { get; set; }

    /// <summary>1..31 — required when Cadence = Monthly.</summary>
    public int? DayOfMonth { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }

    /// <summary>ok | error — last execution outcome, surfaced in "My Scheduled Reports".</summary>
    public string? LastRunStatus { get; set; }

    /// <summary>Short outcome detail (row count or error text) from the last run.</summary>
    public string? LastRunSummary { get; set; }

    /// <summary>True when the last run's notification was dispatched to the creator.</summary>
    public bool? LastRunDelivered { get; set; }

    /// <summary>
    /// Auto-pause after repeated failures — an erroring schedule must not
    /// hammer the engine forever. Paused after 3 consecutive failures.
    /// </summary>
    public bool IsPausedAfterError { get; set; }

    public int ConsecutiveFailures { get; set; }
}

public enum ReportCadence
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2
}