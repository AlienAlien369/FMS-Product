using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>
/// Sensor data retention (decided policy): raw readings (speed, per-tyre
/// pressure) live 30 days — long enough to cover any trip and post-trip review,
/// and raw rows are the only input to rapid-loss detection. Beyond that they are
/// folded into one hourly min/max/avg row per vehicle+sensor+tyre, kept 12
/// months, then dropped. Runs at startup (catch-up) and daily.
/// </summary>
public class SensorRetentionService : BackgroundService
{
    public const string RawDaysKey = "SensorRetention:RawDays";
    public const string RollupMonthsKey = "SensorRetention:RollupMonths";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<SensorRetentionService> _logger;

    public SensorRetentionService(IServiceScopeFactory scopeFactory, IConfiguration config,
        ILogger<SensorRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Catch up on the initial backlog at startup, then daily.
        await RunOnceAsync(ct);
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        while (await timer.WaitForNextTickAsync(ct))
            await RunOnceAsync(ct);
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rawDays = _config.GetValue(RawDaysKey, 30);
            var rollupMonths = _config.GetValue(RollupMonthsKey, 12);
            var now = DateTime.UtcNow;
            await RunRetentionAsync(db, now.AddDays(-rawDays), now.AddMonths(-rollupMonths), ct);
            _logger.LogInformation("Sensor retention: raw < {RawDays}d rolled up + purged, rollups < {Months}mo purged", rawDays, rollupMonths);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sensor retention job failed — will retry on the next run");
        }
    }

    /// <summary>Internal so unit tests can drive the exact retention pass against an in-memory store.</summary>
    internal static async Task RunRetentionAsync(ApplicationDbContext db, DateTime rawCutoff, DateTime rollupCutoff,
        CancellationToken ct = default)
    {
        await RollUpSpeedAsync(db, rawCutoff, ct);
        await RollUpTyresAsync(db, rawCutoff, ct);

        // Raw rows older than the window are dropped (tyre children cascade).
        var stale = await db.TelemetryEvents.Where(e => e.EventTimeUtc < rawCutoff).ToListAsync(ct);
        if (stale.Count > 0) db.TelemetryEvents.RemoveRange(stale);

        var staleRollups = await db.TelemetryRollupsHourly
            .Where(r => r.HourBucketUtc < rollupCutoff).ToListAsync(ct);
        if (staleRollups.Count > 0) db.TelemetryRollupsHourly.RemoveRange(staleRollups);

        await db.SaveChangesAsync(ct);
    }

    private static async Task RollUpSpeedAsync(ApplicationDbContext db, DateTime rawCutoff, CancellationToken ct)
    {
        var buckets = await db.TelemetryEvents.AsNoTracking()
            .Where(e => e.EventTimeUtc < rawCutoff && e.SpeedKmh.HasValue && e.VehicleId.HasValue)
            .GroupBy(e => new
            {
                TenantId = e.TenantId,
                VehicleId = e.VehicleId!.Value,
                Hour = new DateTime(e.EventTimeUtc.Year, e.EventTimeUtc.Month, e.EventTimeUtc.Day,
                    e.EventTimeUtc.Hour, 0, 0, DateTimeKind.Utc)
            })
            .Select(g => new
            {
                g.Key.TenantId, g.Key.VehicleId, g.Key.Hour,
                Min = g.Min(e => e.SpeedKmh!.Value),
                Max = g.Max(e => e.SpeedKmh!.Value),
                Avg = g.Average(e => e.SpeedKmh!.Value),
                Count = g.Count()
            })
            .ToListAsync(ct);
        await UpsertRollupsAsync(db, buckets.Select(b => new TelemetryRollupHourly
        {
            Id = Guid.NewGuid(), TenantId = b.TenantId, VehicleId = b.VehicleId,
            SensorType = "speed", TyrePosition = null, HourBucketUtc = b.Hour,
            MinValue = b.Min, MaxValue = b.Max, AvgValue = b.Avg, ReadingCount = b.Count
        }), "speed", null, ct);
    }

    private static async Task RollUpTyresAsync(ApplicationDbContext db, DateTime rawCutoff, CancellationToken ct)
    {
        var buckets = await db.TyrePressureReadings.AsNoTracking()
            .Where(r => r.EventTimeUtc < rawCutoff)
            .GroupBy(r => new
            {
                TenantId = r.TenantId,
                VehicleId = r.VehicleId,
                Position = r.Position,
                Hour = new DateTime(r.EventTimeUtc.Year, r.EventTimeUtc.Month, r.EventTimeUtc.Day,
                    r.EventTimeUtc.Hour, 0, 0, DateTimeKind.Utc)
            })
            .Select(g => new
            {
                g.Key.TenantId, g.Key.VehicleId, g.Key.Position, g.Key.Hour,
                Min = g.Min(r => r.PressureBar),
                Max = g.Max(r => r.PressureBar),
                Avg = g.Average(r => r.PressureBar),
                Count = g.Count()
            })
            .ToListAsync(ct);
        await UpsertRollupsAsync(db, buckets.Select(b => new TelemetryRollupHourly
        {
            Id = Guid.NewGuid(), TenantId = b.TenantId, VehicleId = b.VehicleId,
            SensorType = "tyre", TyrePosition = b.Position, HourBucketUtc = b.Hour,
            MinValue = b.Min, MaxValue = b.Max, AvgValue = b.Avg, ReadingCount = b.Count
        }), "tyre", null, ct);
    }

    /// <summary>
    /// Merge aggregate buckets into existing rollup rows (a re-run must not
    /// double-count). Existing rows accumulate min/max/weighted-average/count.
    /// </summary>
    private static async Task UpsertRollupsAsync(ApplicationDbContext db,
        IEnumerable<TelemetryRollupHourly> buckets, string sensorType, TyrePosition? tyrePosition, CancellationToken ct)
    {
        var materialized = buckets.ToList();
        if (materialized.Count == 0) return;

        var vehicleIds = materialized.Select(b => b.VehicleId).ToHashSet();
        var existing = await db.TelemetryRollupsHourly
            .Where(r => r.SensorType == sensorType && r.TyrePosition == tyrePosition
                && vehicleIds.Contains(r.VehicleId))
            .ToListAsync(ct);
        var byKey = existing.ToDictionary(r => (r.VehicleId, r.HourBucketUtc));

        foreach (var b in materialized)
        {
            if (byKey.TryGetValue((b.VehicleId, b.HourBucketUtc), out var row))
            {
                row.MinValue = Math.Min(row.MinValue, b.MinValue);
                row.MaxValue = Math.Max(row.MaxValue, b.MaxValue);
                var total = row.ReadingCount + b.ReadingCount;
                row.AvgValue = total > 0 ? (row.AvgValue * row.ReadingCount + b.AvgValue * b.ReadingCount) / total : row.AvgValue;
                row.ReadingCount = total;
            }
            else
            {
                db.TelemetryRollupsHourly.Add(b);
            }
        }
    }
}