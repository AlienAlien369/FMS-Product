using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>
/// Maintenance Management service — the predictive half of the module.
///
/// A MaintenanceSchedule is a recurring service (oil change, tyre rotation, …)
/// with a trigger (mileage / time / engine hours) and an interval. NextDue*
/// points are rolled forward when a preventive MaintenanceRecord completes the
/// schedule; urgency (Ok/DueSoon/Overdue) is computed against the vehicle's
/// current readings whenever odometer/engine hours advance (telemetry) or a
/// service is logged. Due/overdue/breakdown alerts are entitlement-gated and
/// throttled per vehicle+code.
/// </summary>
public class MaintenanceService
{
    public const string DueSoonAlertCode = "maintenance.due_soon";
    public const string OverdueAlertCode = "maintenance.overdue";
    public const string BreakdownAlertCode = "maintenance.breakdown_logged";

    public static readonly TimeSpan DailyThrottle = TimeSpan.FromHours(24);

    private readonly ApplicationDbContext _db;
    private readonly IAlertTypeEnforcement _alertEnforcement;
    private readonly INotificationService? _notificationService;
    private readonly Func<DateTime> _clock;

    public MaintenanceService(ApplicationDbContext db, IAlertTypeEnforcement alertEnforcement,
        INotificationService? notificationService = null, Func<DateTime>? clock = null)
    {
        _db = db;
        _alertEnforcement = alertEnforcement;
        _notificationService = notificationService;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    // ── Schedules ──────────────────────────────────────────────────────────

    public async Task<MaintenanceScheduleDto> CreateScheduleAsync(Guid companyId, CreateMaintenanceScheduleDto dto, string? userId)
    {
        var vehicle = await _db.Vehicles.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == dto.VehicleId && !v.IsDeleted);
        if (vehicle == null || vehicle.CompanyId != companyId)
            throw new InvalidOperationException("VEHICLE_NOT_FOUND");

        var schedule = new MaintenanceSchedule
        {
            Id = Guid.NewGuid(),
            VehicleId = dto.VehicleId,
            CompanyId = companyId,
            TenantId = companyId,
            Title = string.IsNullOrWhiteSpace(dto.Title) ? dto.ServiceType : dto.Title,
            ServiceType = dto.ServiceType,
            TriggerType = dto.TriggerType,
            IntervalValue = dto.IntervalValue,
            DueLeadValue = dto.DueLeadValue,
            CreatedAt = _clock(),
            CreatedBy = userId
        };

        // Seed the first next-due point from the vehicle's current readings so a
        // brand-new schedule is immediately evaluable (no service history yet).
        var now = _clock();
        switch (dto.TriggerType)
        {
            case MaintenanceTriggerType.MileageInterval:
                schedule.NextDueOdometer = (vehicle.OdometerReading ?? 0) + dto.IntervalValue;
                break;
            case MaintenanceTriggerType.TimeInterval:
                schedule.NextDueDate = now.AddDays((double)dto.IntervalValue);
                break;
            case MaintenanceTriggerType.EngineHoursInterval:
                schedule.NextDueEngineHours = (vehicle.EngineHours ?? 0) + dto.IntervalValue;
                break;
        }

        _db.MaintenanceSchedules.Add(schedule);
        await _db.SaveChangesAsync();
        return await ToScheduleDtoAsync(schedule, vehicle.RegistrationNumber, vehicle);
    }

    public async Task<MaintenanceScheduleDto?> UpdateScheduleAsync(Guid companyId, Guid id, UpdateMaintenanceScheduleDto dto, string? userId)
    {
        var schedule = await _db.MaintenanceSchedules.FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);
        if (schedule == null || schedule.CompanyId != companyId) return null;

        if (dto.Title != null) schedule.Title = dto.Title;
        if (dto.ServiceType != null) schedule.ServiceType = dto.ServiceType;
        if (dto.TriggerType.HasValue) schedule.TriggerType = dto.TriggerType.Value;
        if (dto.IntervalValue.HasValue) schedule.IntervalValue = dto.IntervalValue.Value;
        if (dto.DueLeadValue.HasValue) schedule.DueLeadValue = dto.DueLeadValue.Value;
        if (dto.IsActive.HasValue) schedule.IsActive = dto.IsActive.Value;
        schedule.UpdatedAt = _clock();
        schedule.UpdatedBy = userId;

        // Recompute the next-due point from the last-serviced anchor so an
        // interval/trigger edit takes effect immediately.
        RollNextDue(schedule, _clock());
        await _db.SaveChangesAsync();

        var vehicle = await _db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == schedule.VehicleId);
        return await ToScheduleDtoAsync(schedule, vehicle?.RegistrationNumber, vehicle);
    }

    public async Task<bool> DeleteScheduleAsync(Guid companyId, Guid id)
    {
        var schedule = await _db.MaintenanceSchedules.FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);
        if (schedule == null || schedule.CompanyId != companyId) return false;
        _db.MaintenanceSchedules.Remove(schedule);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<MaintenanceScheduleDto>> GetSchedulesAsync(IReadOnlyList<Guid>? scope, Guid? vehicleId)
    {
        var query = _db.MaintenanceSchedules.AsNoTracking()
            .Where(s => !s.IsDeleted && (scope == null || scope.Contains(s.CompanyId)));
        if (vehicleId.HasValue) query = query.Where(s => s.VehicleId == vehicleId.Value);

        var schedules = await query.OrderBy(s => s.VehicleId).ThenBy(s => s.Title).ToListAsync();
        var vehicleIds = schedules.Select(s => s.VehicleId).Distinct().ToList();
        var vehicles = await _db.Vehicles.AsNoTracking()
            .Where(v => vehicleIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id);

        var result = new List<MaintenanceScheduleDto>();
        foreach (var s in schedules)
        {
            vehicles.TryGetValue(s.VehicleId, out var v);
            result.Add(await ToScheduleDtoAsync(s, v?.RegistrationNumber, v));
        }
        return result;
    }

    // ── Records ────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs a service record. A preventive record may complete a schedule
    /// (rolling its last-serviced refs + next due forward); a breakdown record
    /// raises the maintenance.breakdown_logged alert. The vehicle's odometer /
    /// engine hours are mirrored forward when the record reports newer values.
    /// </summary>
    public async Task<MaintenanceRecordDto> LogRecordAsync(Guid companyId, LogMaintenanceRecordDto dto, string? userId)
    {
        var vehicle = await _db.Vehicles
            .FirstOrDefaultAsync(v => v.Id == dto.VehicleId && !v.IsDeleted);
        if (vehicle == null || vehicle.CompanyId != companyId)
            throw new InvalidOperationException("VEHICLE_NOT_FOUND");

        var now = _clock();
        var record = new MaintenanceRecord
        {
            Id = Guid.NewGuid(),
            VehicleId = dto.VehicleId,
            CompanyId = companyId,
            TenantId = companyId,
            Title = dto.Title,
            Description = dto.Description,
            RecordType = dto.RecordType,
            MaintenanceType = dto.RecordType == MaintenanceRecordType.Breakdown ? "corrective" : "preventive",
            MaintenanceScheduleId = dto.RecordType == MaintenanceRecordType.Preventive ? dto.MaintenanceScheduleId : null,
            BreakdownSeverity = dto.BreakdownSeverity,
            DowntimeHours = dto.DowntimeHours,
            RootCause = dto.RootCause,
            Workshop = dto.Workshop,
            Cost = dto.Cost,
            Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "USD" : dto.Currency,
            OdometerAtService = dto.OdometerAtService,
            EngineHoursAtService = dto.EngineHoursAtService,
            CompletedDate = dto.CompletedDate ?? now,
            IsCompleted = true,
            PartsReplaced = dto.PartsReplaced,
            Notes = dto.Notes,
            CreatedAt = now,
            CreatedBy = userId
        };
        _db.MaintenanceRecords.Add(record);

        // Mirror the vehicle's readings forward (maintenance comparisons read these).
        if (dto.OdometerAtService.HasValue && (!vehicle.OdometerReading.HasValue || dto.OdometerAtService.Value > vehicle.OdometerReading.Value))
            vehicle.OdometerReading = (long)dto.OdometerAtService.Value;
        if (dto.EngineHoursAtService.HasValue && (!vehicle.EngineHours.HasValue || dto.EngineHoursAtService.Value > vehicle.EngineHours.Value))
            vehicle.EngineHours = (long)dto.EngineHoursAtService.Value;

        MaintenanceSchedule? schedule = null;
        if (record.MaintenanceScheduleId.HasValue && dto.RecordType == MaintenanceRecordType.Preventive)
        {
            schedule = await _db.MaintenanceSchedules.FirstOrDefaultAsync(s => s.Id == dto.MaintenanceScheduleId.Value && !s.IsDeleted);
            if (schedule != null)
            {
                schedule.LastServiceOdometer = dto.OdometerAtService ?? vehicle.OdometerReading;
                schedule.LastServiceEngineHours = dto.EngineHoursAtService ?? vehicle.EngineHours;
                schedule.LastServiceDate = record.CompletedDate;
                schedule.LastMaintenanceRecordId = record.Id;
                RollNextDue(schedule, now);
                schedule.UpdatedAt = now;
                schedule.UpdatedBy = userId;
            }
        }

        if (record.RecordType == MaintenanceRecordType.Breakdown)
        {
            await StageAlertAsync(companyId, vehicle.Id, BreakdownAlertCode,
                $"Breakdown logged: {record.Title} — vehicle {vehicle.RegistrationNumber}",
                $"A {(string.IsNullOrWhiteSpace(dto.BreakdownSeverity) ? "breakdown" : dto.BreakdownSeverity + " breakdown")} was logged for vehicle {vehicle.RegistrationNumber}"
                    + (dto.RootCause != null ? $". Root cause: {dto.RootCause}" : string.Empty)
                    + (dto.DowntimeHours is > 0 ? $" ({dto.DowntimeHours:0.#} h downtime)" : string.Empty) + ".",
                AlertSeverity.High, null, null, now);
        }

        await _db.SaveChangesAsync();
        return ToRecordDto(record, vehicle.RegistrationNumber, schedule?.Title);
    }

    public async Task<bool> DeleteRecordAsync(Guid companyId, Guid id)
    {
        var record = await _db.MaintenanceRecords.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (record == null || record.CompanyId != companyId) return false;
        _db.MaintenanceRecords.Remove(record);
        await _db.SaveChangesAsync();
        return true;
    }

    // ── Due evaluation ─────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates one vehicle's active schedules against current readings
    /// (telemetry odometer/engine hours, or the vehicle's stored values) and
    /// fires due_soon/overdue alerts. Called from the ingestion pipeline on
    /// every odometer/hour advance and from the log-service path.
    /// </summary>
    public async Task EvaluateVehicleAsync(Guid companyId, Guid vehicleId, double? odometerKm = null,
        double? engineHours = null, DateTime? at = null)
    {
        var vehicle = await _db.Vehicles.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == vehicleId && !v.IsDeleted);
        if (vehicle == null) return;

        var now = at ?? _clock();
        var schedules = await _db.MaintenanceSchedules.AsNoTracking()
            .Where(s => s.VehicleId == vehicleId && s.IsActive && !s.IsDeleted)
            .ToListAsync();
        if (schedules.Count == 0) return;

        foreach (var schedule in schedules)
        {
            var (status, detail) = ComputeDueStatus(schedule,
                (long?)(odometerKm ?? vehicle.OdometerReading),
                (long?)(engineHours ?? vehicle.EngineHours),
                now);

            if (status == MaintenanceDueStatus.DueSoon)
            {
                await StageAlertAsync(companyId, vehicleId, DueSoonAlertCode,
                    $"Maintenance due soon — {schedule.Title}",
                    $"Vehicle {vehicle.RegistrationNumber}: {schedule.Title} is {detail}.",
                    AlertSeverity.Low, null, null, now);
            }
            else if (status == MaintenanceDueStatus.Overdue)
            {
                await StageAlertAsync(companyId, vehicleId, OverdueAlertCode,
                    $"Maintenance overdue — {schedule.Title}",
                    $"Vehicle {vehicle.RegistrationNumber}: {schedule.Title} is {detail}.",
                    AlertSeverity.High, null, null, now);
            }
        }
    }

    /// <summary>
    /// Pure due-status computation shared by the alert path and the read path
    /// (fleet overview / vehicle detail), so the UI badge can never disagree
    /// with the alert severity.
    /// </summary>
    public static (MaintenanceDueStatus Status, string Detail) ComputeDueStatus(
        MaintenanceSchedule schedule, long? currentOdometerKm, long? currentEngineHours, DateTime now)
    {
        switch (schedule.TriggerType)
        {
            case MaintenanceTriggerType.MileageInterval:
            {
                if (!schedule.NextDueOdometer.HasValue)
                {
                    // No service history and no seeded anchor → can't evaluate yet.
                    return (MaintenanceDueStatus.NoData, "no odometer baseline");
                }
                if (!currentOdometerKm.HasValue)
                    return (MaintenanceDueStatus.NoData, "no current odometer reading");

                var remaining = schedule.NextDueOdometer.Value - currentOdometerKm.Value;
                var lead = schedule.DueLeadValue ?? 0;
                if (remaining <= 0)
                    return (MaintenanceDueStatus.Overdue, $"overdue by {Math.Abs(remaining):N0} km");
                if (remaining <= lead)
                    return (MaintenanceDueStatus.DueSoon, $"due in {remaining:N0} km");
                return (MaintenanceDueStatus.Ok, $"due in {remaining:N0} km");
            }
            case MaintenanceTriggerType.TimeInterval:
            {
                if (!schedule.NextDueDate.HasValue)
                    return (MaintenanceDueStatus.NoData, "no due date");
                var leadDays = (double)(schedule.DueLeadValue ?? 0);
                var remaining = (schedule.NextDueDate.Value - now).TotalDays;
                if (remaining <= 0)
                    return (MaintenanceDueStatus.Overdue, $"overdue by {Math.Abs(remaining):0} days");
                if (remaining <= leadDays)
                    return (MaintenanceDueStatus.DueSoon, $"due in {remaining:0} days");
                return (MaintenanceDueStatus.Ok, $"due in {remaining:0} days");
            }
            case MaintenanceTriggerType.EngineHoursInterval:
            {
                if (!schedule.NextDueEngineHours.HasValue)
                    return (MaintenanceDueStatus.NoData, "no engine-hours baseline");
                if (!currentEngineHours.HasValue)
                    return (MaintenanceDueStatus.NoData, "no current engine hours");

                var remaining = schedule.NextDueEngineHours.Value - currentEngineHours.Value;
                var lead = schedule.DueLeadValue ?? 0;
                if (remaining <= 0)
                    return (MaintenanceDueStatus.Overdue, $"overdue by {Math.Abs(remaining):N0} engine hours");
                if (remaining <= lead)
                    return (MaintenanceDueStatus.DueSoon, $"due in {remaining:N0} engine hours");
                return (MaintenanceDueStatus.Ok, $"due in {remaining:N0} engine hours");
            }
            default:
                return (MaintenanceDueStatus.NoData, "unknown trigger");
        }
    }

    /// <summary>Fleet-wide overview: every active schedule with computed urgency, sorted by it.</summary>
    public async Task<MaintenanceFleetOverviewDto> GetFleetOverviewAsync(IReadOnlyList<Guid>? scope)
    {
        var schedules = await _db.MaintenanceSchedules.AsNoTracking()
            .Where(s => s.IsActive && !s.IsDeleted && (scope == null || scope.Contains(s.CompanyId)))
            .OrderBy(s => s.VehicleId).ThenBy(s => s.Title)
            .ToListAsync();
        var vehicleIds = schedules.Select(s => s.VehicleId).Distinct().ToList();
        var vehicles = await _db.Vehicles.AsNoTracking()
            .Where(v => vehicleIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id);
        // Live readings: the materialized telemetry state (updated on every
        // accepted fix) wins over the Vehicle row, which only moves on manual
        // edits / logged services — maintenance urgency must reflect real
        // distance driven, not the last time someone opened the vehicle form.
        var states = await _db.TelemetryStates.AsNoTracking()
            .Where(t => vehicleIds.Contains(t.VehicleId))
            .ToDictionaryAsync(t => t.VehicleId);

        var now = _clock();
        var overview = new MaintenanceFleetOverviewDto { TotalSchedules = schedules.Count };
        foreach (var s in schedules)
        {
            vehicles.TryGetValue(s.VehicleId, out var v);
            var dto = await ToScheduleDtoAsync(s, v?.RegistrationNumber, v);
            states.TryGetValue(s.VehicleId, out var state);
            var (status, detail) = ComputeDueStatus(s,
                (long?)(state?.OdometerKm ?? v?.OdometerReading),
                (long?)(state?.EngineHours ?? v?.EngineHours),
                now);
            dto.DueStatus = status;
            dto.DueStatusDetail = detail;
            overview.Schedules.Add(dto);
            switch (status)
            {
                case MaintenanceDueStatus.Overdue: overview.OverdueCount++; break;
                case MaintenanceDueStatus.DueSoon: overview.DueSoonCount++; break;
                case MaintenanceDueStatus.Ok: overview.OkCount++; break;
                default: overview.NoDataCount++; break;
            }
        }

        overview.Schedules = overview.Schedules
            .OrderBy(s => s.DueStatus == MaintenanceDueStatus.Overdue ? 0 : s.DueStatus == MaintenanceDueStatus.DueSoon ? 1 : 2)
            .ThenBy(s => s.VehicleRegistration)
            .ToList();

        var breakdowns = await _db.MaintenanceRecords.AsNoTracking()
            .Where(r => !r.IsDeleted && r.RecordType == MaintenanceRecordType.Breakdown
                && r.CompletedDate >= now.AddDays(-30) && (scope == null || scope.Contains(r.CompanyId)))
            .OrderByDescending(r => r.CompletedDate)
            .Take(50)
            .ToListAsync();
        var breakdownVehicleIds = breakdowns.Select(r => r.VehicleId).Distinct().ToList();
        var breakdownVehicles = await _db.Vehicles.AsNoTracking()
            .Where(v => breakdownVehicleIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id);
        overview.RecentBreakdowns = breakdowns
            .Select(r => ToRecordDto(r,
                breakdownVehicles.TryGetValue(r.VehicleId, out var bv) ? bv.RegistrationNumber : null,
                null))
            .ToList();
        overview.BreakdownCount30d = breakdowns.Count;

        return overview;
    }

    /// <summary>One vehicle's schedules + service history for the detail tab.</summary>
    public async Task<MaintenanceVehicleDetailDto> GetVehicleDetailAsync(IReadOnlyList<Guid>? scope, Guid vehicleId)
    {
        var vehicle = await _db.Vehicles.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == vehicleId && !v.IsDeleted && (scope == null || scope.Contains(v.CompanyId)));
        if (vehicle == null) return null!;

        var now = _clock();
        var detail = new MaintenanceVehicleDetailDto
        {
            VehicleId = vehicleId,
            VehicleRegistration = vehicle.RegistrationNumber
        };

        var schedules = await _db.MaintenanceSchedules.AsNoTracking()
            .Where(s => s.VehicleId == vehicleId && s.IsActive && !s.IsDeleted)
            .OrderBy(s => s.Title)
            .ToListAsync();
        var state = await _db.TelemetryStates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.VehicleId == vehicleId);
        foreach (var s in schedules)
        {
            var dto = await ToScheduleDtoAsync(s, vehicle.RegistrationNumber, vehicle);
            var (status, statusDetail) = ComputeDueStatus(s,
                (long?)(state?.OdometerKm ?? vehicle.OdometerReading),
                (long?)(state?.EngineHours ?? vehicle.EngineHours),
                now);
            dto.DueStatus = status;
            dto.DueStatusDetail = statusDetail;
            detail.Schedules.Add(dto);
            if (status == MaintenanceDueStatus.Overdue) detail.OverdueCount++;
            else if (status == MaintenanceDueStatus.DueSoon) detail.DueSoonCount++;
        }

        detail.Records = (await _db.MaintenanceRecords.AsNoTracking()
            .Where(r => !r.IsDeleted && r.VehicleId == vehicleId)
            .OrderByDescending(r => r.CompletedDate)
            .Take(100)
            .Select(r => new { r, ScheduleTitle = r.MaintenanceSchedule != null ? r.MaintenanceSchedule.Title : null })
            .ToListAsync())
            .Select(x => ToRecordDto(x.r, vehicle.RegistrationNumber, x.ScheduleTitle))
            .ToList();

        return detail;
    }

    /// <summary>List of records for the fleet page (optionally filtered).</summary>
    public async Task<List<MaintenanceRecordDto>> GetRecordsAsync(IReadOnlyList<Guid>? scope, Guid? vehicleId = null, int take = 100)
    {
        var query = _db.MaintenanceRecords.AsNoTracking()
            .Where(r => !r.IsDeleted && (scope == null || scope.Contains(r.CompanyId)));
        if (vehicleId.HasValue) query = query.Where(r => r.VehicleId == vehicleId.Value);

        var rows = await query
            .OrderByDescending(r => r.CompletedDate)
            .Take(take)
            .Select(r => new { r, Registration = r.Vehicle != null ? r.Vehicle.RegistrationNumber : null, ScheduleTitle = r.MaintenanceSchedule != null ? r.MaintenanceSchedule.Title : null })
            .ToListAsync();
        return rows.Select(x => ToRecordDto(x.r, x.Registration, x.ScheduleTitle)).ToList();
    }

    // ── Internals ──────────────────────────────────────────────────────────

    /// <summary>Rolls NextDue* forward from the schedule's last-serviced anchor.</summary>
    private static void RollNextDue(MaintenanceSchedule schedule, DateTime now)
    {
        switch (schedule.TriggerType)
        {
            case MaintenanceTriggerType.MileageInterval:
                schedule.NextDueOdometer = (schedule.LastServiceOdometer ?? schedule.NextDueOdometer) + schedule.IntervalValue;
                break;
            case MaintenanceTriggerType.TimeInterval:
                schedule.NextDueDate = (schedule.LastServiceDate ?? now).AddDays((double)schedule.IntervalValue);
                break;
            case MaintenanceTriggerType.EngineHoursInterval:
                schedule.NextDueEngineHours = (schedule.LastServiceEngineHours ?? schedule.NextDueEngineHours) + schedule.IntervalValue;
                break;
        }
    }

    private async Task<MaintenanceScheduleDto> ToScheduleDtoAsync(MaintenanceSchedule s, string? registration, Vehicle? vehicle)
    {
        return new MaintenanceScheduleDto
        {
            Id = s.Id,
            VehicleId = s.VehicleId,
            VehicleRegistration = registration,
            VehicleName = vehicle?.Name,
            CompanyId = s.CompanyId,
            Title = s.Title,
            ServiceType = s.ServiceType,
            TriggerType = s.TriggerType,
            IntervalValue = s.IntervalValue,
            DueLeadValue = s.DueLeadValue,
            LastServiceOdometer = s.LastServiceOdometer,
            LastServiceEngineHours = s.LastServiceEngineHours,
            LastServiceDate = s.LastServiceDate,
            NextDueOdometer = s.NextDueOdometer,
            NextDueEngineHours = s.NextDueEngineHours,
            NextDueDate = s.NextDueDate,
            IsActive = s.IsActive,
            DueStatus = MaintenanceDueStatus.NoData,
            DueStatusDetail = null
        };
    }

    private static MaintenanceRecordDto ToRecordDto(MaintenanceRecord r, string? registration, string? scheduleTitle)
    {
        return new MaintenanceRecordDto
        {
            Id = r.Id,
            VehicleId = r.VehicleId,
            VehicleRegistration = registration,
            CompanyId = r.CompanyId,
            Title = r.Title,
            Description = r.Description,
            RecordType = r.RecordType,
            MaintenanceScheduleId = r.MaintenanceScheduleId,
            MaintenanceScheduleTitle = scheduleTitle,
            BreakdownSeverity = r.BreakdownSeverity,
            DowntimeHours = r.DowntimeHours,
            RootCause = r.RootCause,
            Workshop = r.Workshop,
            Cost = r.Cost,
            Currency = r.Currency,
            OdometerAtService = r.OdometerAtService,
            EngineHoursAtService = r.EngineHoursAtService,
            CompletedDate = r.CompletedDate,
            IsCompleted = r.IsCompleted,
            PartsReplaced = r.PartsReplaced,
            Notes = r.Notes,
            CreatedAt = r.CreatedAt
        };
    }

    private async Task StageAlertAsync(Guid companyId, Guid vehicleId, string code, string title, string message,
        AlertSeverity severity, double? latitude, double? longitude, DateTime at)
    {
        if (!await _alertEnforcement.IsEntitledAsync(companyId, code)) return;
        if (await RecentAlertExistsAsync(companyId, vehicleId, code, at, DailyThrottle)) return;

        _db.Alerts.Add(new Alert
        {
            Id = Guid.NewGuid(),
            AlertType = code,
            Severity = severity,
            Title = title,
            Message = message,
            CompanyId = companyId,
            TenantId = companyId,
            VehicleId = vehicleId,
            Latitude = latitude,
            Longitude = longitude,
            CreatedAt = _clock()
        });
        if (_notificationService != null)
        {
            await _notificationService.NotifyCompanyAdminsAsync(companyId, "alert.fired", title, message,
                (int)severity, "Vehicle", vehicleId, $"/vehicles/{vehicleId}");
        }
    }

    private async Task<bool> RecentAlertExistsAsync(Guid companyId, Guid vehicleId, string code, DateTime at, TimeSpan window)
    {
        var since = _clock().Add(-window);
        return await _db.Alerts.AsNoTracking().AnyAsync(a => !a.IsDeleted
            && a.CompanyId == companyId && a.VehicleId == vehicleId
            && a.AlertType == code && a.CreatedAt >= since);
    }
}