using Freebuff.Platform.Api.Authorization;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Infrastructure.CompanyScope;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Shared.Extensions;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class VehiclesController : ControllerBase
{
    private readonly VehicleService _vehicleService;
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly FleetPolicyService _fleetPolicies;

    public VehiclesController(VehicleService vehicleService, ApplicationDbContext db, ITenantContext tenant,
        FleetPolicyService fleetPolicies)
    {
        _vehicleService = vehicleService;
        _db = db;
        _tenant = tenant;
        _fleetPolicies = fleetPolicies;
    }

    [HttpGet]
    [RequirePermission("vehicle.view")]
    public async Task<ActionResult<ApiResponse<PagedResult<VehicleDto>>>> GetAll([FromQuery] PagedRequest filter, [FromQuery] int? status = null)
    {
        var result = await _vehicleService.GetListAsync(filter, status);
        return Ok(ApiResponse<PagedResult<VehicleDto>>.Ok(result));
    }

    [HttpGet("stats")]
    [RequirePermission("vehicle.view")]
    public async Task<ActionResult<ApiResponse<object>>> GetStats()
    {
        var query = _db.Vehicles.AsNoTracking().Where(v => !v.IsDeleted);
        // Query-side: effective scope = X-Company-Scope ∩ permitted set.
        query = query.InEffectiveCompanyScope(_tenant.Scope, v => v.CompanyId);

        var total = await query.CountAsync();
        var active = await query.CountAsync(v => v.Status == Domain.Enums.VehicleStatus.Active);
        var inactive = await query.CountAsync(v => v.Status == Domain.Enums.VehicleStatus.Inactive);
        var maintenance = await query.CountAsync(v => v.Status == Domain.Enums.VehicleStatus.InMaintenance);
        var retired = await query.CountAsync(v => v.Status == Domain.Enums.VehicleStatus.Retired);
        var stolen = await query.CountAsync(v => v.Status == Domain.Enums.VehicleStatus.Stolen);
        var withDriver = await query.CountAsync(v => v.DriverId != null);
        var withDevice = await query.CountAsync(v => v.DeviceImei != null);

        return Ok(ApiResponse<object>.Ok(new
        {
            total, active, inactive, maintenance, retired, stolen,
            withDriver, withDevice,
            unassigned = total - withDriver
        }));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission("vehicle.view")]
    public async Task<ActionResult<ApiResponse<VehicleDto>>> GetById(Guid id)
    {
        var result = await _vehicleService.GetByIdAsync(id);
        if (result == null) return NotFound(ApiResponse<VehicleDto>.Fail("NOT_FOUND", "Vehicle not found"));
        return Ok(ApiResponse<VehicleDto>.Ok(result));
    }

    [HttpPost]
    [RequirePermission("vehicle.create")]
    public async Task<ActionResult<ApiResponse<VehicleDto>>> Create([FromBody] CreateVehicleDto dto)
    {

        var userId = User.GetUserIdString();
        var result = await _vehicleService.CreateAsync(dto, userId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<VehicleDto>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("vehicle.update")]
    public async Task<ActionResult<ApiResponse<VehicleDto>>> Update(Guid id, [FromBody] UpdateVehicleDto dto)
    {

        var userId = User.GetUserIdString();
        var result = await _vehicleService.UpdateAsync(id, dto, userId);
        if (result == null) return NotFound(ApiResponse<VehicleDto>.Fail("NOT_FOUND", "Vehicle not found"));
        return Ok(ApiResponse<VehicleDto>.Ok(result));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("vehicle.delete")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id, [FromQuery] string? reason = null)
    {

        var userId = User.GetUserIdString();
        var deleted = await _vehicleService.SoftDeleteAsync(id, userId, reason);
        if (!deleted) return NotFound(ApiResponse.Fail("NOT_FOUND", "Vehicle not found"));
        return Ok(ApiResponse.Ok(message: "Vehicle deleted"));
    }

    [HttpPost("{id:guid}/restore")]
    [RequirePermission("vehicle.update")]
    public async Task<ActionResult<ApiResponse>> Restore(Guid id)
    {

        var userId = User.GetUserIdString();
        var restored = await _vehicleService.RestoreAsync(id, userId);
        if (!restored) return NotFound(ApiResponse.Fail("NOT_FOUND", "Vehicle not found or not deleted"));
        return Ok(ApiResponse.Ok(message: "Vehicle restored"));
    }

    /// <summary>
    /// Live sensor snapshot: current speed vs the resolved speed policy, the
    /// device-reported governor limit (informational), and per-tyre pressure
    /// status from the latest TPMS-bearing telemetry event. Devices/vendors
    /// that don't report a sensor surface it as "not supported" (GovernorSupported
    /// / TyresSupported false) — never a false zero.
    /// </summary>
    [HttpGet("{id:guid}/sensors")]
    [RequirePermission("vehicle.view")]
    public async Task<ActionResult<ApiResponse<VehicleSensorsDto>>> GetSensors(Guid id)
    {
        var query = _db.Vehicles.AsNoTracking().Where(v => v.Id == id && !v.IsDeleted);
        if (!_tenant.IsSuperAdmin && _tenant.TenantId.HasValue)
            query = query.Where(v => v.CompanyId == _tenant.TenantId.Value);
        var vehicle = await query.FirstOrDefaultAsync();
        if (vehicle == null) return NotFound(ApiResponse<VehicleSensorsDto>.Fail("NOT_FOUND", "Vehicle not found"));

        // Latest speed-carrying event + latest event with tyre readings (may be different events).
        var speedEvent = await _db.TelemetryEvents.AsNoTracking()
            .Where(e => e.VehicleId == id && e.SpeedKmh.HasValue)
            .OrderByDescending(e => e.EventTimeUtc)
            .FirstOrDefaultAsync();
        var tyreEvent = await _db.TelemetryEvents.AsNoTracking()
            .Include(e => e.TyrePressureReadings)
            .Where(e => e.VehicleId == id && e.TyrePressureReadings.Any())
            .OrderByDescending(e => e.EventTimeUtc)
            .FirstOrDefaultAsync();

        var speedPolicy = await _fleetPolicies.GetSpeedPolicyAsync(vehicle.CompanyId, vehicle);
        var tyrePolicy = await _fleetPolicies.GetTyrePolicyAsync(vehicle.CompanyId, vehicle);

        var dto = new VehicleSensorsDto
        {
            SpeedKmh = speedEvent?.SpeedKmh,
            SpeedGovernorLimitKmh = speedEvent?.SpeedGovernorLimitKmh,
            PolicySpeedMaxKmh = speedPolicy.MaxKmh,
            TyrePolicyMinBar = tyrePolicy.MinBar,
            TyrePolicyMaxBar = tyrePolicy.MaxBar,
            GovernorSupported = speedEvent?.SpeedGovernorLimitKmh.HasValue == true,
            TyresSupported = tyreEvent != null,
            LastUpdate = (speedEvent?.EventTimeUtc ?? tyreEvent?.EventTimeUtc),
            SpeedStatus = speedEvent == null ? "noData"
                : !speedPolicy.MaxKmh.HasValue ? "noPolicy"
                : speedEvent.SpeedKmh!.Value > speedPolicy.MaxKmh.Value ? "over"
                : "ok"
        };

        if (tyreEvent != null && tyrePolicy.MinBar.HasValue && tyrePolicy.MaxBar.HasValue)
        {
            // Previous reading per tyre (within the rapid-loss window) so the
            // live surface uses the SAME rule as the alert pipeline — a tyre
            // the producer flagged critical for rapid loss must not render
            // as a mere warning here.
            var previousReadings = await _db.TyrePressureReadings.AsNoTracking()
                .Where(r => r.VehicleId == id && r.EventTimeUtc >= tyreEvent.EventTimeUtc - SensorPolicyAlertProducer.RapidLossWindow
                    && r.EventTimeUtc < tyreEvent.EventTimeUtc)
                .OrderByDescending(r => r.EventTimeUtc)
                .ToListAsync();
            var latestByPosition = previousReadings
                .GroupBy(r => r.Position)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var r in tyreEvent.TyrePressureReadings)
            {
                var hasPrevious = latestByPosition.TryGetValue(r.Position, out var prev);
                var (severity, _) = SensorPolicyAlertProducer.ClassifyShared(r.PressureBar,
                    tyrePolicy.MinBar.Value, tyrePolicy.MaxBar.Value,
                    hasPrevious ? prev.PressureBar : null,
                    hasPrevious ? prev.EventTimeUtc : (DateTime?)null,
                    tyreEvent.EventTimeUtc);
                dto.Tyres.Add(new TyreSensorDto
                {
                    Position = (int)r.Position,
                    PositionName = SensorPolicyAlertProducer.PositionLabel(r.Position),
                    PressureBar = r.PressureBar,
                    TemperatureC = r.TemperatureC,
                    Status = severity == null ? "ok"
                        : severity == Domain.Enums.AlertSeverity.High ? "critical"
                        : "warning"
                });
            }
        }
        return Ok(ApiResponse<VehicleSensorsDto>.Ok(dto));
    }

    [HttpGet("{id:guid}/audit")]
    [RequirePermission("vehicle.view")]
    public async Task<ActionResult<ApiResponse<List<AuditEntryDto>>>> GetAuditHistory(Guid id)
    {
        var result = await _vehicleService.GetAuditHistoryAsync(id);
        return Ok(ApiResponse<List<AuditEntryDto>>.Ok(result));
    }
}
