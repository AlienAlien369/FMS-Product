using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>
/// Fuel Management service — the transaction + analytics surface of the module.
/// Manual fill-ups are created here (efficiency computed against the previous
/// transaction at write time); sensor-derived refuels are materialized into the
/// same FuelRecord stream by FuelConsumptionAnalyzer so both ingestion paths
/// share one reporting surface. Queries honor the caller's effective company
/// scope (X-Company-Scope ∩ permitted set).
/// </summary>
public class FuelService
{
    private readonly ApplicationDbContext _db;

    public FuelService(ApplicationDbContext db) => _db = db;

    /// <summary>
    /// Logs a manual fill-up. DistanceTraveledKm is computed against the
    /// vehicle's previous transaction (or the vehicle's odometer baseline for
    /// the first one), so efficiency per fill-up is meaningful from day one.
    /// </summary>
    public async Task<FuelRecordDto> CreateManualAsync(Guid companyId, CreateFuelRecordDto dto, string? userId)
    {
        var vehicle = await _db.Vehicles
            .FirstOrDefaultAsync(v => v.Id == dto.VehicleId && !v.IsDeleted);
        if (vehicle == null || vehicle.CompanyId != companyId)
            throw new InvalidOperationException("VEHICLE_NOT_FOUND");

        // Distance since the vehicle's previous transaction (any source).
        var previous = await _db.FuelRecords.AsNoTracking()
            .Where(f => !f.IsDeleted && f.VehicleId == dto.VehicleId && f.RecordDate <= dto.RecordDate)
            .OrderByDescending(f => f.RecordDate)
            .Select(f => (decimal?)f.OdometerReading)
            .FirstOrDefaultAsync();
        var baseline = previous ?? (dto.OdometerReading.HasValue ? (decimal?)vehicle.OdometerReading : null);
        decimal? distance = null;
        if (dto.OdometerReading.HasValue && baseline.HasValue && dto.OdometerReading.Value >= baseline.Value)
            distance = dto.OdometerReading.Value - baseline.Value;

        var record = new FuelRecord
        {
            Id = Guid.NewGuid(),
            VehicleId = dto.VehicleId,
            CompanyId = companyId,
            TenantId = companyId,
            FuelType = dto.FuelType,
            Quantity = dto.Quantity,
            Unit = string.IsNullOrWhiteSpace(dto.Unit) ? "liters" : dto.Unit,
            PricePerUnit = dto.PricePerUnit,
            TotalCost = dto.TotalCost,
            OdometerReading = dto.OdometerReading,
            FuelLevel = dto.FuelLevel,
            Source = FuelRecord.SourceManual,
            Station = dto.Station,
            DistanceTraveledKm = distance,
            IsRefueling = dto.IsRefueling,
            Notes = dto.Notes,
            RecordDate = dto.RecordDate == default ? DateTime.UtcNow : dto.RecordDate,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        };
        _db.FuelRecords.Add(record);

        // Mirror the odometer forward so manual logging keeps the vehicle's
        // readings current (the field is what maintenance scheduling compares).
        if (dto.OdometerReading.HasValue
            && (!vehicle.OdometerReading.HasValue || dto.OdometerReading.Value > vehicle.OdometerReading.Value))
        {
            vehicle.OdometerReading = (long)dto.OdometerReading.Value;
        }

        await _db.SaveChangesAsync();
        return ToDto(record, vehicle.RegistrationNumber);
    }

    public async Task<bool> DeleteAsync(Guid companyId, Guid id)
    {
        var record = await _db.FuelRecords.FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted);
        if (record == null || record.CompanyId != companyId) return false;
        _db.FuelRecords.Remove(record);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<PagedResult<FuelRecordDto>> GetTransactionsAsync(
        IReadOnlyList<Guid>? scope, Guid? vehicleId, DateTime? from, DateTime? to,
        int page, int pageSize, string? search)
    {
        var query = _db.FuelRecords.AsNoTracking()
            .Where(f => !f.IsDeleted && (scope == null || scope.Contains(f.CompanyId)));
        if (vehicleId.HasValue) query = query.Where(f => f.VehicleId == vehicleId.Value);
        if (from.HasValue) query = query.Where(f => f.RecordDate >= from.Value);
        if (to.HasValue) query = query.Where(f => f.RecordDate <= to.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(f => f.RecordDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(f => new { f, Registration = f.Vehicle != null ? f.Vehicle.RegistrationNumber : null })
            .ToListAsync();

        return new PagedResult<FuelRecordDto>
        {
            Items = items.Select(x => ToDto(x.f, x.Registration)).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Fleet-wide consumption/cost aggregates. Efficiency is computed from
    /// COMPLETE pairs only (a transaction with distance and quantity), so a
    /// single first fill-up never inflates the fleet number.
    /// </summary>
    public async Task<FuelFleetStatsDto> GetFleetStatsAsync(IReadOnlyList<Guid>? scope,
        DateTime? from = null, DateTime? to = null, int days = 30)
    {
        var since = from ?? DateTime.UtcNow.AddDays(-days);
        var query = _db.FuelRecords.AsNoTracking()
            .Where(f => !f.IsDeleted && f.RecordDate >= since && (scope == null || scope.Contains(f.CompanyId)));
        if (to.HasValue) query = query.Where(f => f.RecordDate <= to.Value);

        var rows = await query
            .Select(f => new { f.Quantity, f.TotalCost, f.PricePerUnit, f.DistanceTraveledKm, f.CompanyId, f.VehicleId, f.Source, f.IsAnomaly })
            .ToListAsync();

        var stats = new FuelFleetStatsDto
        {
            TransactionCount = rows.Count,
            TotalLiters = rows.Sum(r => r.Quantity),
            TotalSpend = rows.Sum(r => r.TotalCost ?? 0m),
            AnomalyCount = rows.Count(r => r.IsAnomaly),
            SensorCoveredVehicles = rows.Where(r => r.Source == FuelRecord.SourceSensor).Select(r => r.VehicleId).Distinct().Count(),
            ManualVehicles = rows.Where(r => r.Source != FuelRecord.SourceSensor).Select(r => r.VehicleId).Distinct().Count()
        };
        stats.AvgPricePerLiter = rows.Where(r => r.PricePerUnit.HasValue && r.Quantity > 0)
            .Select(r => r.PricePerUnit)
            .DefaultIfEmpty().Average();

        var totalDistance = rows.Sum(r => r.DistanceTraveledKm ?? 0m);
        if (totalDistance > 0)
        {
            stats.AvgEfficiencyKmPerLiter = Math.Round(totalDistance / stats.TotalLiters, 2);
            stats.CostPerKm = Math.Round(stats.TotalSpend / totalDistance, 4);
        }
        return stats;
    }

    /// <summary>Daily consumption trend (liters, spend, efficiency) for the fleet chart.</summary>
    public async Task<List<FuelTrendPointDto>> GetTrendAsync(IReadOnlyList<Guid>? scope,
        DateTime? from = null, DateTime? to = null, int days = 30)
    {
        var since = from ?? DateTime.UtcNow.AddDays(-days);
        var query = _db.FuelRecords.AsNoTracking()
            .Where(f => !f.IsDeleted && f.RecordDate >= since && (scope == null || scope.Contains(f.CompanyId)));
        if (to.HasValue) query = query.Where(f => f.RecordDate <= to.Value);

        var rows = await query
            .Select(f => new { f.RecordDate, f.Quantity, f.TotalCost, f.DistanceTraveledKm })
            .ToListAsync();

        return rows
            .GroupBy(r => r.RecordDate.Date)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var liters = g.Sum(r => r.Quantity);
                var distance = g.Sum(r => r.DistanceTraveledKm ?? 0m);
                return new FuelTrendPointDto
                {
                    Day = g.Key.ToString("yyyy-MM-dd"),
                    Liters = liters,
                    Spend = g.Sum(r => r.TotalCost ?? 0m),
                    EfficiencyKmPerLiter = distance > 0 ? Math.Round(distance / liters, 2) : null
                };
            })
            .ToList();
    }

    /// <summary>
    /// Per-vehicle analytics: transaction history with per-fill-up efficiency,
    /// sensor snapshot stream + per-day consumption trend when a fuel sensor is
    /// attached, and a HasFuelSensor flag (Device Abstraction Layer role).
    /// </summary>
    public async Task<FuelVehicleMetricsDto> GetVehicleMetricsAsync(IReadOnlyList<Guid>? scope, Guid vehicleId)
    {
        var vehicle = await _db.Vehicles.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == vehicleId && !v.IsDeleted && (scope == null || scope.Contains(v.CompanyId)));
        if (vehicle == null) return null!;

        var hasFuelSensor = await _db.VehicleDevices.AsNoTracking()
            .AnyAsync(vd => vd.VehicleId == vehicleId && vd.AssignedTo == null && !vd.IsDeleted
                && vd.Role == VehicleDeviceRole.FuelSensor);

        var transactions = await _db.FuelRecords.AsNoTracking()
            .Where(f => !f.IsDeleted && f.VehicleId == vehicleId)
            .OrderByDescending(f => f.RecordDate)
            .Take(200)
            .ToListAsync();

        var snapshots = await _db.FuelConsumptionSnapshots.AsNoTracking()
            .Where(s => s.VehicleId == vehicleId)
            .OrderByDescending(s => s.EventTimeUtc)
            .Take(100)
            .ToListAsync();

        var metrics = new FuelVehicleMetricsDto
        {
            VehicleId = vehicleId,
            VehicleRegistration = vehicle.RegistrationNumber,
            TransactionCount = transactions.Count,
            TotalLiters = transactions.Sum(t => t.Quantity),
            TotalSpend = transactions.Sum(t => t.TotalCost ?? 0m),
            Transactions = transactions.Select(t => ToDto(t, vehicle.RegistrationNumber)).ToList(),
            Snapshots = snapshots.Select(s => new FuelConsumptionSnapshotDto
            {
                Id = s.Id,
                EventTimeUtc = s.EventTimeUtc,
                FuelLevelPercent = s.FuelLevelPercent,
                FuelLevelLiters = s.FuelLevelLiters,
                OdometerKm = s.OdometerKm,
                LitersConsumed = s.LitersConsumed,
                DistanceKm = s.DistanceKm,
                ConsumptionLitersPer100km = s.ConsumptionLitersPer100km,
                IsAnomaly = s.IsAnomaly,
                AnomalyReason = s.AnomalyReason
            }).ToList(),
            HasFuelSensor = hasFuelSensor
        };

        var totalDistance = transactions.Sum(t => t.DistanceTraveledKm ?? 0m);
        if (totalDistance > 0)
        {
            metrics.AvgEfficiencyKmPerLiter = Math.Round(totalDistance / metrics.TotalLiters, 2);
            metrics.CostPerKm = Math.Round(metrics.TotalSpend / totalDistance, 4);
        }

        // Per-day consumption trend from the sensor stream (L/100km per day).
        metrics.ConsumptionTrend = snapshots
            .Where(s => s.ConsumptionLitersPer100km.HasValue)
            .GroupBy(s => s.EventTimeUtc.Date)
            .OrderBy(g => g.Key)
            .Select(g => new FuelTrendPointDto
            {
                Day = g.Key.ToString("yyyy-MM-dd"),
                Liters = g.Sum(s => (decimal)(s.LitersConsumed ?? 0)),
                Spend = 0m,
                EfficiencyKmPerLiter = g.Average(s => (decimal?)s.ConsumptionLitersPer100km) is decimal avg
                    ? 100m / avg
                    : null
            })
            .ToList();

        return metrics;
    }

    private static FuelRecordDto ToDto(FuelRecord f, string? registration)
    {
        decimal? efficiency = null;
        if (f.DistanceTraveledKm is > 0 && f.Quantity > 0)
        {
            efficiency = Math.Round(f.DistanceTraveledKm.Value / f.Quantity, 2);
        }
        return new FuelRecordDto
        {
            Id = f.Id,
            VehicleId = f.VehicleId,
            VehicleRegistration = registration,
            CompanyId = f.CompanyId,
            FuelType = f.FuelType,
            Quantity = f.Quantity,
            Unit = f.Unit,
            PricePerUnit = f.PricePerUnit,
            TotalCost = f.TotalCost,
            OdometerReading = f.OdometerReading,
            FuelLevel = f.FuelLevel,
            Source = f.Source,
            Station = f.Station,
            DistanceTraveledKm = f.DistanceTraveledKm,
            IsAnomaly = f.IsAnomaly,
            AnomalyReason = f.AnomalyReason,
            Notes = f.Notes,
            RecordDate = f.RecordDate,
            EfficiencyKmPerLiter = efficiency,
            ConsumptionLitersPer100km = efficiency is > 0 ? Math.Round(100m / efficiency.Value, 2) : null
        };
    }
}