namespace Freebuff.Platform.Application.DTOs;

// ── Report Engine ──────────────────────────────────────────────────────────

public sealed class ReportColumnDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    /// <summary>string | number | currency | date — drives frontend rendering + CSV/PDF formatting.</summary>
    public string Type { get; set; } = "string";
}

public sealed class ReportSeriesDto
{
    public string Label { get; set; } = string.Empty;
    public List<ReportSeriesPointDto> Points { get; set; } = new();
}

public sealed class ReportSeriesPointDto
{
    public string X { get; set; } = string.Empty;
    public double Y { get; set; }
}

public sealed class ReportSummaryItemDto
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// A fully materialized report: columns describe the table, rows are
/// name/value maps (values are primitives: string/double/decimal/bool/null),
/// series feed charts, summary feeds the stat cards above the table.
/// </summary>
public sealed class ReportResultDto
{
    public string ReportType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<ReportColumnDto> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public List<ReportSeriesDto> Series { get; set; } = new();
    public List<ReportSummaryItemDto> Summary { get; set; } = new();
}

/// <summary>Filters every report accepts — same shape for on-demand runs and scheduled reports.</summary>
public sealed class ReportRequestDto
{
    public string ReportType { get; set; } = string.Empty;
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<Guid> VehicleIds { get; set; } = new();
    public List<Guid> DriverIds { get; set; } = new();
    public Guid? CompanyId { get; set; }
}

public sealed class ReportCatalogItemDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    /// <summary>Permissions the caller must hold to see this category (engine-side gate).</summary>
    public List<string> RequiresView { get; set; } = new();
    public List<string> RequiresExport { get; set; } = new();
    /// <summary>True when the caller can export this category.</summary>
    public bool CanExport { get; set; }
    public bool CanSchedule { get; set; }
}

// ── Scheduled reports ──────────────────────────────────────────────────────

public sealed class CreateScheduledReportDto
{
    public string ReportType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public ReportRequestDto Parameters { get; set; } = new();
    public string Cadence { get; set; } = "daily"; // daily | weekly | monthly
    public int? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
}

public sealed class UpdateScheduledReportDto
{
    public string? Title { get; set; }
    public ReportRequestDto? Parameters { get; set; }
    public string? Cadence { get; set; }
    public int? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public bool? IsActive { get; set; }
}

public sealed class ScheduledReportDto
{
    public Guid Id { get; set; }
    public string ReportType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Cadence { get; set; } = "daily";
    public int? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public bool IsActive { get; set; }
    public DateTime? NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public string? LastRunStatus { get; set; }
    public string? LastRunSummary { get; set; }
    public ReportRequestDto Parameters { get; set; } = new();
}

// ── Alerts page ────────────────────────────────────────────────────────────

public sealed class AlertListItemDto
{
    public Guid Id { get; set; }
    public string AlertType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Message { get; set; }
    public int Severity { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? VehicleId { get; set; }
    public string? VehicleRegistration { get; set; }
    public Guid? DriverId { get; set; }
    public string? DriverName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
}

public sealed class AlertStatsDto
{
    public int TotalCount { get; set; }
    public int OpenCount { get; set; }
    public int AcknowledgedCount { get; set; }
    public Dictionary<string, int> ByType { get; set; } = new();
    public Dictionary<string, int> BySeverity { get; set; } = new();
}