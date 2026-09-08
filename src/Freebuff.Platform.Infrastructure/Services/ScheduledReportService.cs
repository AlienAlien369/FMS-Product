using System.Text.Json;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>
/// CRUD + execution lifecycle for recurring reports. A schedule captures the
/// report type, parameters and cadence; when it comes due, the hosted runner
/// re-runs the report through the Report Engine and delivers the output via the
/// Notification system — no separate email stack, exactly as designed.
///
/// The caller's company scope at configuration time is frozen into the stored
/// parameters (scopeIds), so a background run reproduces exactly what the user
/// saw when they saved the schedule — no live user context needed at 2am.
/// </summary>
public class ScheduledReportService
{
    private readonly ApplicationDbContext _db;
    private readonly IReportEngine _engine;

    public ScheduledReportService(ApplicationDbContext db, IReportEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    public async Task<ScheduledReportDto> CreateAsync(Guid companyId, Guid userId, CreateScheduledReportDto dto,
        IReadOnlyList<Guid>? effectiveScope)
    {
        if (_engine.Find(dto.ReportType) == null)
            throw new InvalidOperationException($"Unknown report type '{dto.ReportType}'");

        var (cadence, dayOfWeek, dayOfMonth) = ParseCadence(dto.Cadence, dto.DayOfWeek, dto.DayOfMonth);

        var schedule = new ScheduledReport
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            TenantId = companyId,
            CreatedByUserId = userId,
            ReportType = dto.ReportType,
            Title = string.IsNullOrWhiteSpace(dto.Title) ? _engine.Find(dto.ReportType)!.Name : dto.Title.Trim(),
            ParametersJson = SerializeParameters(dto.Parameters, effectiveScope),
            Cadence = cadence,
            DayOfWeek = dayOfWeek,
            DayOfMonth = dayOfMonth,
            IsActive = true,
            NextRunAt = ComputeNextRun(DateTime.UtcNow, cadence, dayOfWeek, dayOfMonth),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.ScheduledReports.Add(schedule);
        await _db.SaveChangesAsync();
        return ToDto(schedule);
    }

    public async Task<ScheduledReportDto?> UpdateAsync(Guid companyId, Guid userId, Guid id, UpdateScheduledReportDto dto,
        IReadOnlyList<Guid>? effectiveScope)
    {
        var schedule = await _db.ScheduledReports
            .FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId && !s.IsDeleted);
        if (schedule == null) return null;

        if (!string.IsNullOrWhiteSpace(dto.Title)) schedule.Title = dto.Title.Trim();
        if (dto.Parameters != null) schedule.ParametersJson = SerializeParameters(dto.Parameters, effectiveScope);
        if (!string.IsNullOrWhiteSpace(dto.Cadence))
        {
            var (cadence, dow, dom) = ParseCadence(dto.Cadence!, dto.DayOfWeek, dto.DayOfMonth);
            schedule.Cadence = cadence;
            schedule.DayOfWeek = dow;
            schedule.DayOfMonth = dom;
            schedule.NextRunAt = ComputeNextRun(DateTime.UtcNow, cadence, dow, dom);
        }
        if (dto.IsActive.HasValue)
        {
            schedule.IsActive = dto.IsActive.Value;
            if (dto.IsActive.Value)
            {
                schedule.IsPausedAfterError = false;
                schedule.ConsecutiveFailures = 0;
                schedule.NextRunAt ??= ComputeNextRun(DateTime.UtcNow, schedule.Cadence, schedule.DayOfWeek, schedule.DayOfMonth);
            }
        }
        schedule.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(schedule);
    }

    public async Task<bool> DeleteAsync(Guid companyId, Guid id)
    {
        var schedule = await _db.ScheduledReports
            .FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId && !s.IsDeleted);
        if (schedule == null) return false;
        schedule.IsDeleted = true;
        schedule.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<ScheduledReportDto>> GetMineAsync(Guid companyId, Guid userId)
    {
        var items = await _db.ScheduledReports.AsNoTracking()
            .Where(s => s.CompanyId == companyId && s.CreatedByUserId == userId && !s.IsDeleted)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
        return items.Select(ToDto).ToList();
    }

    /// <summary>All schedules in a company (used by tests + potential admin views).</summary>
    public async Task<List<ScheduledReportDto>> GetCompanyAsync(Guid companyId)
    {
        var items = await _db.ScheduledReports.AsNoTracking()
            .Where(s => s.CompanyId == companyId && !s.IsDeleted)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
        return items.Select(ToDto).ToList();
    }

    /// <summary>
    /// Run every schedule whose NextRunAt has arrived. Returns (executed, delivered)
    /// counts for observability/tests.
    /// </summary>
    public async Task<(int Executed, int Delivered)> RunDueAsync(DateTime now, INotificationService notifications)
    {
        var due = await _db.ScheduledReports
            .Where(s => !s.IsDeleted && s.IsActive && !s.IsPausedAfterError
                && s.NextRunAt != null && s.NextRunAt <= now)
            .ToListAsync();

        var executed = 0;
        var delivered = 0;
        foreach (var schedule in due)
        {
            executed++;
            try
            {
                var (parameters, scope) = DeserializeParameters(schedule.ParametersJson);
                var result = await _engine.RunAsync(schedule.ReportType, parameters, scope);

                schedule.LastRunAt = now;
                schedule.LastRunStatus = "ok";
                schedule.LastRunSummary = $"{result.Rows.Count} rows · {result.Summary.Count} summary metrics";
                schedule.ConsecutiveFailures = 0;
                schedule.IsPausedAfterError = false;

                var digest = BuildDigest(result);
                await notifications.NotifyUserAsync(
                    schedule.CompanyId, schedule.CreatedByUserId, "report.scheduled_delivered",
                    $"{schedule.Title} — generated",
                    digest,
                    (int)AlertSeverity.Info,
                    relatedEntityType: "ScheduledReport",
                    relatedEntityId: schedule.Id,
                    actionUrl: "/reports");
                schedule.LastRunDelivered = true;
                delivered++;
            }
            catch (Exception ex)
            {
                schedule.LastRunStatus = "error";
                schedule.LastRunSummary = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;
                schedule.ConsecutiveFailures++;
                if (schedule.ConsecutiveFailures >= 3)
                {
                    schedule.IsPausedAfterError = true;
                    schedule.IsActive = false;
                }
            }
            schedule.NextRunAt = ComputeNextRun(now, schedule.Cadence, schedule.DayOfWeek, schedule.DayOfMonth);
            schedule.UpdatedAt = now;
        }
        if (due.Count > 0) await _db.SaveChangesAsync();
        return (executed, delivered);
    }

    // ── Internals ─────────────────────────────────────────────────────────

    private static (ReportCadence, int?, int?) ParseCadence(string cadence, int? dayOfWeek, int? dayOfMonth)
    {
        var c = cadence.Trim().ToLowerInvariant();
        if (c == "weekly")
        {
            if (dayOfWeek is < 0 or > 6) throw new InvalidOperationException("DayOfWeek must be 0..6 for weekly cadence");
            return (ReportCadence.Weekly, dayOfWeek ?? 1, null);
        }
        if (c == "monthly")
        {
            if (dayOfMonth is < 1 or > 31) throw new InvalidOperationException("DayOfMonth must be 1..31 for monthly cadence");
            return (ReportCadence.Monthly, null, dayOfMonth ?? 1);
        }
        return (ReportCadence.Daily, null, null);
    }

    public static DateTime ComputeNextRun(DateTime now, ReportCadence cadence, int? dayOfWeek, int? dayOfMonth)
    {
        switch (cadence)
        {
            case ReportCadence.Daily:
                return now.Date.AddDays(1);
            case ReportCadence.Weekly:
            {
                var target = dayOfWeek ?? 1;
                var daysUntil = (target - (int)now.DayOfWeek + 7) % 7;
                if (daysUntil == 0) daysUntil = 7;
                return now.Date.AddDays(daysUntil);
            }
            case ReportCadence.Monthly:
            {
                var day = Math.Clamp(dayOfMonth ?? 1, 1, 28);
                var next = new DateTime(now.Year, now.Month, 1).AddMonths(1);
                var lastDay = DateTime.DaysInMonth(next.Year, next.Month);
                return new DateTime(next.Year, next.Month, Math.Min(day, lastDay));
            }
            default:
                return now.Date.AddDays(1);
        }
    }

    private static string SerializeParameters(ReportRequestDto parameters, IReadOnlyList<Guid>? effectiveScope)
    {
        var copy = new Dictionary<string, object?>
        {
            ["from"] = parameters.From,
            ["to"] = parameters.To,
            ["vehicleIds"] = parameters.VehicleIds,
            ["driverIds"] = parameters.DriverIds,
            ["companyId"] = parameters.CompanyId
        };
        if (effectiveScope == null)
            copy["scopeIds"] = "ALL";
        else
            copy["scopeIds"] = effectiveScope.ToList();
        return JsonSerializer.Serialize(copy);
    }

    private static (ReportRequestDto Request, IReadOnlyList<Guid>? Scope) DeserializeParameters(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var req = new ReportRequestDto
        {
            From = root.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.String ? f.GetDateTime() : (DateTime?)null,
            To = root.TryGetProperty("to", out var t) && t.ValueKind == JsonValueKind.String ? t.GetDateTime() : (DateTime?)null,
            VehicleIds = ReadGuids(root, "vehicleIds"),
            DriverIds = ReadGuids(root, "driverIds"),
            CompanyId = root.TryGetProperty("companyId", out var c) && c.ValueKind == JsonValueKind.String ? c.GetGuid() : (Guid?)null
        };

        IReadOnlyList<Guid>? scope = null;
        if (root.TryGetProperty("scopeIds", out var scopeEl) && scopeEl.ValueKind == JsonValueKind.Array)
        {
            scope = scopeEl.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetGuid()).ToList();
        }
        return (req, scope);
    }

    private static List<Guid> ReadGuids(JsonElement root, string prop)
    {
        if (!root.TryGetProperty(prop, out var el) || el.ValueKind != JsonValueKind.Array) return new List<Guid>();
        return el.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetGuid()).ToList();
    }

    private static string BuildDigest(ReportResultDto result)
    {
        var parts = new List<string> { $"{result.Title}: {result.Rows.Count} rows" };
        foreach (var s in result.Summary.Take(6))
            parts.Add($"{s.Label}: {s.Value}");
        return string.Join(" · ", parts);
    }

    private static ScheduledReportDto ToDto(ScheduledReport s)
    {
        return new ScheduledReportDto
        {
            Id = s.Id,
            ReportType = s.ReportType,
            Title = s.Title,
            Cadence = s.Cadence switch
            {
                ReportCadence.Weekly => "weekly",
                ReportCadence.Monthly => "monthly",
                _ => "daily"
            },
            DayOfWeek = s.DayOfWeek,
            DayOfMonth = s.DayOfMonth,
            IsActive = s.IsActive,
            NextRunAt = s.NextRunAt,
            LastRunAt = s.LastRunAt,
            LastRunStatus = s.LastRunStatus,
            LastRunSummary = s.LastRunSummary,
            Parameters = DeserializeParameters(s.ParametersJson).Request
        };
    }
}

/// <summary>
/// Background worker that ticks once a minute and runs any due scheduled
/// report. Delivery goes through INotificationService — the same in-app
/// channel every other event uses (email/SMS/push plug in later without
/// touching this loop).
/// </summary>
public class ScheduledReportRunner : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledReportRunner> _logger;

    public ScheduledReportRunner(IServiceScopeFactory scopeFactory, ILogger<ScheduledReportRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var engine = scope.ServiceProvider.GetRequiredService<IReportEngine>();
                var service = new ScheduledReportService(db, engine);
                var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
                var (executed, delivered) = await service.RunDueAsync(DateTime.UtcNow, notifications);
                if (executed > 0)
                    _logger.LogInformation("[Reports] Scheduled runner executed {Executed} reports, delivered {Delivered} notifications", executed, delivered);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Reports] Scheduled runner iteration failed");
            }
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}