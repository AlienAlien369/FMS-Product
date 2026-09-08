using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Application.DTOs;

public class CreateMaintenanceScheduleDto
{
    public Guid VehicleId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ServiceType { get; set; } = "general_inspection";
    public MaintenanceTriggerType TriggerType { get; set; } = MaintenanceTriggerType.MileageInterval;
    public decimal IntervalValue { get; set; }
    public decimal? DueLeadValue { get; set; }
}

public class UpdateMaintenanceScheduleDto
{
    public string? Title { get; set; }
    public string? ServiceType { get; set; }
    public MaintenanceTriggerType? TriggerType { get; set; }
    public decimal? IntervalValue { get; set; }
    public decimal? DueLeadValue { get; set; }
    public bool? IsActive { get; set; }
}

public class MaintenanceScheduleDto
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public string? VehicleRegistration { get; set; }
    public string? VehicleName { get; set; }
    public Guid CompanyId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ServiceType { get; set; } = "general_inspection";
    public MaintenanceTriggerType TriggerType { get; set; }
    public decimal IntervalValue { get; set; }
    public decimal? DueLeadValue { get; set; }
    public decimal? LastServiceOdometer { get; set; }
    public decimal? LastServiceEngineHours { get; set; }
    public DateTime? LastServiceDate { get; set; }
    public decimal? NextDueOdometer { get; set; }
    public decimal? NextDueEngineHours { get; set; }
    public DateTime? NextDueDate { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Current urgency computed against the vehicle's latest readings (Ok/DueSoon/Overdue/NoData).</summary>
    public MaintenanceDueStatus DueStatus { get; set; } = MaintenanceDueStatus.NoData;

    /// <summary>Human explanation of the due status (e.g. "due in 320 km" / "overdue by 1,400 km").</summary>
    public string? DueStatusDetail { get; set; }
}

public class LogMaintenanceRecordDto
{
    public Guid VehicleId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public MaintenanceRecordType RecordType { get; set; } = MaintenanceRecordType.Preventive;
    /// <summary>Preventive records may complete a schedule — roll its next-due forward.</summary>
    public Guid? MaintenanceScheduleId { get; set; }
    public string? BreakdownSeverity { get; set; }
    public decimal? DowntimeHours { get; set; }
    public string? RootCause { get; set; }
    public string? Workshop { get; set; }
    public decimal? Cost { get; set; }
    public string? Currency { get; set; } = "USD";
    public decimal? OdometerAtService { get; set; }
    public decimal? EngineHoursAtService { get; set; }
    public DateTime? CompletedDate { get; set; }
    public string? PartsReplaced { get; set; }
    public string? Notes { get; set; }
}

public class MaintenanceRecordDto
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public string? VehicleRegistration { get; set; }
    public Guid CompanyId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public MaintenanceRecordType RecordType { get; set; }
    public Guid? MaintenanceScheduleId { get; set; }
    public string? MaintenanceScheduleTitle { get; set; }
    public string? BreakdownSeverity { get; set; }
    public decimal? DowntimeHours { get; set; }
    public string? RootCause { get; set; }
    public string? Workshop { get; set; }
    public decimal? Cost { get; set; }
    public string? Currency { get; set; }
    public decimal? OdometerAtService { get; set; }
    public decimal? EngineHoursAtService { get; set; }
    public DateTime? CompletedDate { get; set; }
    public bool IsCompleted { get; set; }
    public string? PartsReplaced { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class MaintenanceFleetOverviewDto
{
    public int TotalSchedules { get; set; }
    public int OverdueCount { get; set; }
    public int DueSoonCount { get; set; }
    public int OkCount { get; set; }
    public int NoDataCount { get; set; }
    public int BreakdownCount30d { get; set; }
    /// <summary>Every schedule with a computed status, sorted by urgency (overdue first).</summary>
    public List<MaintenanceScheduleDto> Schedules { get; set; } = new();
    /// <summary>Recent breakdown records (30 days), newest first.</summary>
    public List<MaintenanceRecordDto> RecentBreakdowns { get; set; } = new();
}

public class MaintenanceVehicleDetailDto
{
    public Guid VehicleId { get; set; }
    public string? VehicleRegistration { get; set; }
    public List<MaintenanceScheduleDto> Schedules { get; set; } = new();
    public List<MaintenanceRecordDto> Records { get; set; } = new();
    public int OverdueCount { get; set; }
    public int DueSoonCount { get; set; }
}