using System.Security.Claims;
using Freebuff.Platform.Api.Authorization;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.CompanyScope;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FleetRoute = Freebuff.Platform.Domain.Entities.Route;

namespace Freebuff.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/trips")]
[Authorize]
public class TripsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly TargetCompanyResolver _targetCompany;
    private readonly TripLifecycleService _lifecycle;

    public TripsController(ApplicationDbContext db, ITenantContext tenant, TargetCompanyResolver targetCompany, TripLifecycleService lifecycle)
    {
        _db = db;
        _tenant = tenant;
        _targetCompany = targetCompany;
        _lifecycle = lifecycle;
    }

    private Guid GetTenantId() => Guid.Parse(User.FindFirstValue("tenant_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsSuperAdmin() => User.IsInRole("SuperAdmin") || User.Claims.Any(c => c.Type == "is_super_admin" && c.Value == "true");
    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ── List / stats ────────────────────────────────────────────────────────

    [HttpGet]
    [RequirePermission("trip.view")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] int? status = null, [FromQuery] int? type = null, [FromQuery] Guid? vehicleId = null,
        [FromQuery] Guid? driverId = null, [FromQuery] bool? delayed = null,
        [FromQuery] string? sortBy = null, [FromQuery] bool sortDesc = false)
    {
        // Query-side: effective scope = X-Company-Scope ∩ permitted set (list view).
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var query = _db.Trips.AsNoTracking()
            .Where(t => !t.IsDeleted && (scope == null || scope.Contains(t.CompanyId)))
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(t => t.Name.Contains(search)
                || (t.Description != null && t.Description.Contains(search))
                || t.StartLocation.Contains(search)
                || (t.EndLocation != null && t.EndLocation.Contains(search)));
        if (status.HasValue) query = query.Where(t => (int)t.Status == status.Value);
        if (type.HasValue) query = query.Where(t => (int)t.Type == type.Value);
        if (vehicleId.HasValue) query = query.Where(t => t.VehicleId == vehicleId.Value);
        if (driverId.HasValue) query = query.Where(t => t.DriverId == driverId.Value);
        if (delayed.HasValue) query = query.Where(t => t.IsDelayed == delayed.Value);

        query = sortBy?.ToLower() switch
        {
            "name" => sortDesc ? query.OrderByDescending(t => t.Name) : query.OrderBy(t => t.Name),
            "status" => sortDesc ? query.OrderByDescending(t => t.Status) : query.OrderBy(t => t.Status),
            "scheduledstarttime" => sortDesc ? query.OrderByDescending(t => t.ScheduledStartTime) : query.OrderBy(t => t.ScheduledStartTime),
            "createdat" => sortDesc ? query.OrderByDescending(t => t.CreatedAt) : query.OrderBy(t => t.CreatedAt),
            _ => query.OrderByDescending(t => t.ScheduledStartTime ?? t.CreatedAt)
        };

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(t => new TripDto
            {
                Id = t.Id, Name = t.Name, Description = t.Description,
                Status = (int)t.Status, StatusName = t.Status.ToString(),
                IsDelayed = t.IsDelayed, DelayReason = t.DelayReason, CancelReason = t.CancelReason,
                Type = (int)t.Type, TypeName = t.Type.ToString(),
                CompanyName = t.Company.Name,
                VehicleId = t.VehicleId, VehicleName = t.Vehicle.RegistrationNumber,
                DriverId = t.DriverId, DriverName = t.Driver.FirstName + " " + t.Driver.LastName,
                RouteId = t.RouteId, RouteName = t.Route != null ? t.Route.Name : null,
                ScheduledStartTime = t.ScheduledStartTime, ScheduledEndTime = t.ScheduledEndTime,
                ActualStartTime = t.ActualStartTime, ActualEndTime = t.ActualEndTime,
                PlannedDistance = t.PlannedDistance, ActualDistance = t.ActualDistance,
                PlannedDuration = t.PlannedDuration, ActualDuration = t.ActualDuration,
                MaxSpeed = t.MaxSpeed, AverageSpeed = t.AverageSpeed,
                FuelUsedLiters = t.FuelUsedLiters, IdleMinutes = t.IdleMinutes,
                RouteGeometry = t.RouteGeometry,
                CorridorEnabled = t.CorridorEnabled, CorridorBufferMeters = t.CorridorBufferMeters,
                DeviationThresholdMinutes = t.DeviationThresholdMinutes,
                WaypointCount = t.TripWaypoints.Count,
                GeofenceCount = t.TripGeofences.Count(),
                CheckpointCount = t.TripGeofences.Count(x => x.Role == TripGeofenceRole.Checkpoint),
                RestrictedZoneCount = t.TripGeofences.Count(x => x.Role == TripGeofenceRole.RestrictedZone),
                BoundaryZoneCount = t.TripGeofences.Count(x => x.Role == TripGeofenceRole.StartZone || x.Role == TripGeofenceRole.EndZone),
                CreatedAt = t.CreatedAt
            }).ToListAsync();

        return Ok(new ApiResponse<PagedResult<TripDto>>
        {
            Success = true,
            Data = new PagedResult<TripDto> { Items = items, TotalCount = total, Page = page, PageSize = pageSize }
        });
    }

    [HttpGet("stats")]
    [RequirePermission("trip.view")]
    public async Task<IActionResult> GetStats()
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var query = _db.Trips.AsNoTracking().Where(t => !t.IsDeleted && (scope == null || scope.Contains(t.CompanyId)));

        return Ok(new ApiResponse<object>
        {
            Success = true,
            Data = new
            {
                Total = await query.CountAsync(),
                Scheduled = await query.CountAsync(t => t.Status == TripStatus.Scheduled),
                InProgress = await query.CountAsync(t => t.Status == TripStatus.InProgress),
                Completed = await query.CountAsync(t => t.Status == TripStatus.Completed),
                Delayed = await query.CountAsync(t => t.IsDelayed),
                Cancelled = await query.CountAsync(t => t.Status == TripStatus.Cancelled),
                TotalDistance = await query.SumAsync(t => t.ActualDistance ?? t.PlannedDistance ?? 0)
            }
        });
    }

    // ── Detail ──────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}")]
    [RequirePermission("trip.view")]
    public async Task<IActionResult> Get(Guid id)
    {
        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var t = await _db.Trips.AsNoTracking()
            .Include(t => t.Company)
            .Include(t => t.Vehicle)
            .Include(t => t.Driver)
            .Include(t => t.Route)
            .Include(t => t.TripWaypoints)
            .Include(t => t.TripGeofences).ThenInclude(g => g.Geofence)
            .Include(t => t.StatusHistory)
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted && (isSuperAdmin || t.CompanyId == tenantId));
        if (t == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Trip not found." });

        return Ok(new ApiResponse<TripDto> { Success = true, Data = ToDto(t) });
    }

    // ── Create ──────────────────────────────────────────────────────────────

    [HttpPost]
    [RequirePermission("trip.create")]
    public async Task<IActionResult> Create([FromBody] CreateTripDto dto)
    {
        var companyId = await _targetCompany.ResolveAsync(dto.CompanyId);

        // Vehicle + Driver must belong to the target company.
        var vehicle = await _db.Vehicles.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == dto.VehicleId && !v.IsDeleted && v.CompanyId == companyId);
        if (vehicle == null)
            return BadRequest(new ApiResponse<object> { Success = false, Message = "Vehicle does not exist in the target company." });
        var driver = await _db.Drivers.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == dto.DriverId && !d.IsDeleted && d.CompanyId == companyId);
        if (driver == null)
            return BadRequest(new ApiResponse<object> { Success = false, Message = "Driver does not exist in the target company." });

        // Optional linked route — must be the same company; its geofences/corridor
        // are inherited as the trip's starting template.
        FleetRoute? route = null;
        if (dto.RouteId.HasValue)
        {
            route = await _db.Routes.Include(r => r.RouteGeofences)
                .FirstOrDefaultAsync(r => r.Id == dto.RouteId.Value && !r.IsDeleted && r.CompanyId == companyId);
            if (route == null)
                return BadRequest(new ApiResponse<object> { Success = false, Message = "Route does not exist in the target company." });
        }

        // Waypoints: from the payload, else inherited from the linked route.
        var waypoints = BuildWaypoints(dto.Waypoints);
        var inheritedWaypoints = false;
        if (waypoints.Count == 0 && route != null)
        {
            waypoints = RouteWaypointsToTrip(route);
            inheritedWaypoints = true;
        }
        var wpErrors = TripLifecycleService.ValidateWaypoints(waypoints, (TripType)dto.Type);
        if (wpErrors.Count > 0)
            return BadRequest(new ApiResponse<object> { Success = false, Message = string.Join(" ", wpErrors) });

        // Double-booking guard (hard error) at create time.
        var conflicts = await _lifecycle.AssignmentConflictsAsync(companyId, dto.VehicleId, dto.DriverId);
        if (conflicts.Count > 0)
            return BadRequest(new ApiResponse<object> { Success = false, Message = string.Join(" ", conflicts) });

        // Geofence links: DTO direct links + inherited route links (dedupe).
        var links = new List<(Guid GeofenceId, int Role, int? SequenceOrder)>();
        if (route != null)
        {
            foreach (var rg in route.RouteGeofences.Where(rg => !rg.IsDeleted))
                links.Add((rg.GeofenceId, (int)rg.Role, rg.SequenceOrder));
        }
        if (dto.GeofenceLinks != null)
            links.AddRange(dto.GeofenceLinks.Select(l => (l.GeofenceId, l.Role, l.SequenceOrder)));
        var linkErrors = TripLifecycleService.ValidateGeofenceLinks(links);
        if (linkErrors.Count > 0)
            return BadRequest(new ApiResponse<object> { Success = false, Message = string.Join(" ", linkErrors) });

        var geofenceIds = links.Select(l => l.GeofenceId).ToList();
        var validGeofences = await _db.Geofences.AsNoTracking()
            .Where(g => geofenceIds.Contains(g.Id) && !g.IsDeleted)
            .Select(g => new { g.Id, g.CompanyId })
            .ToListAsync();
        var gfById = validGeofences.ToDictionary(v => v.Id);
        foreach (var (gid, _, _) in links)
        {
            if (!gfById.TryGetValue(gid, out var gf) || gf.CompanyId != companyId)
                return BadRequest(new ApiResponse<object> { Success = false, Message = "Every linked geofence must belong to the trip's company." });
        }

        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Description = dto.Description,
            Type = (TripType)dto.Type,
            IsRoundTrip = dto.Type == (int)TripType.Round,
            Status = TripStatus.Draft,
            CompanyId = companyId,
            TenantId = companyId,
            VehicleId = dto.VehicleId,
            DriverId = dto.DriverId,
            RouteId = route?.Id,
            ScheduledStartTime = dto.ScheduledStartTime,
            StartLocation = waypoints[0].Name,
            StartLatitude = waypoints[0].Latitude,
            StartLongitude = waypoints[0].Longitude,
            EndLocation = waypoints[^1].Name,
            EndLatitude = waypoints[^1].Latitude,
            EndLongitude = waypoints[^1].Longitude,
            RouteGeometry = dto.RouteGeometry ?? route?.RouteGeometry,
            CorridorEnabled = dto.CorridorEnabled ?? route?.CorridorEnabled ?? false,
            CorridorBufferMeters = dto.CorridorBufferMeters ?? route?.CorridorBufferMeters,
            DeviationThresholdMinutes = dto.DeviationThresholdMinutes ?? route?.DeviationThresholdMinutes,
            PlannedDistance = 0
        };
        _db.Trips.Add(trip);
        foreach (var w in waypoints) w.TenantId = companyId;
        trip.TripWaypoints = waypoints;
        foreach (var (gid, role, seq) in links)
        {
            trip.TripGeofences.Add(new TripGeofence
            {
                Id = Guid.NewGuid(),
                TripId = trip.Id,
                TenantId = companyId,
                GeofenceId = gid,
                Role = (TripGeofenceRole)role,
                SequenceOrder = seq
            });
        }
        trip.StatusHistory.Add(new TripStatusHistory
        {
            Id = Guid.NewGuid(),
            TripId = trip.Id,
            TenantId = companyId,
            FromStatus = TripStatus.Draft,
            ToStatus = TripStatus.Draft,
            Reason = "Trip created" + (inheritedWaypoints ? " (waypoints inherited from route)" : ""),
            Source = "manual",
            ChangedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        _targetCompany.Audit(AuditAction.Create, EntityType.Trip, trip.Id, trip.Name, null, companyId);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = trip.Id }, new ApiResponse<TripDto>
        {
            Success = true, Message = "Trip created (draft).",
            Data = new TripDto { Id = trip.Id, Name = trip.Name, Status = (int)trip.Status, StatusName = trip.Status.ToString() }
        });
    }

    // ── Update ──────────────────────────────────────────────────────────────

    [HttpPut("{id:guid}")]
    [RequirePermission("trip.update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTripDto dto)
    {
        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var t = await _db.Trips
            .Include(t => t.TripGeofences)
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted && (isSuperAdmin || t.CompanyId == tenantId));
        if (t == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Trip not found." });
        if (t.Status is TripStatus.Completed or TripStatus.Cancelled or TripStatus.Aborted)
            return BadRequest(new ApiResponse<object> { Success = false, Message = $"A {t.Status.ToString().ToLowerInvariant()} trip cannot be edited." });

        if (dto.Name != null) t.Name = dto.Name;
        if (dto.Description != null) t.Description = dto.Description;
        if (dto.Type.HasValue) { t.Type = (TripType)dto.Type.Value; t.IsRoundTrip = dto.Type.Value == (int)TripType.Round; }
        if (dto.ScheduledStartTime.HasValue) t.ScheduledStartTime = dto.ScheduledStartTime;
        if (dto.RouteGeometry != null) t.RouteGeometry = dto.RouteGeometry;
        if (dto.CorridorEnabled.HasValue) t.CorridorEnabled = dto.CorridorEnabled.Value;
        if (dto.CorridorBufferMeters.HasValue) t.CorridorBufferMeters = dto.CorridorBufferMeters.Value;
        if (dto.DeviationThresholdMinutes.HasValue) t.DeviationThresholdMinutes = dto.DeviationThresholdMinutes.Value;

        // Assignment changes re-validate ownership + double-booking (excluding self).
        if (dto.VehicleId.HasValue || dto.DriverId.HasValue)
        {
            var vId = dto.VehicleId ?? t.VehicleId;
            var dId = dto.DriverId ?? t.DriverId;
            var vOk = await _db.Vehicles.AsNoTracking().AnyAsync(v => v.Id == vId && !v.IsDeleted && v.CompanyId == t.CompanyId);
            var dOk = await _db.Drivers.AsNoTracking().AnyAsync(d => d.Id == dId && !d.IsDeleted && d.CompanyId == t.CompanyId);
            if (!vOk || !dOk)
                return BadRequest(new ApiResponse<object> { Success = false, Message = "Vehicle and driver must belong to the trip's company." });
            var conflicts = await _lifecycle.AssignmentConflictsAsync(t.CompanyId, vId, dId, t.Id);
            if (conflicts.Count > 0)
                return BadRequest(new ApiResponse<object> { Success = false, Message = string.Join(" ", conflicts) });
            if (dto.VehicleId.HasValue) t.VehicleId = dto.VehicleId.Value;
            if (dto.DriverId.HasValue) t.DriverId = dto.DriverId.Value;
        }

        // Route re-link: inherit the new route's geofences/corridor/geometry.
        if (dto.RouteId.HasValue && dto.RouteId.Value != t.RouteId)
        {
            var route = await _db.Routes.Include(r => r.RouteGeofences)
                .FirstOrDefaultAsync(r => r.Id == dto.RouteId.Value && !r.IsDeleted && r.CompanyId == t.CompanyId);
            if (route == null)
                return BadRequest(new ApiResponse<object> { Success = false, Message = "Route does not exist in the trip's company." });
            t.RouteId = route.Id;
            if (string.IsNullOrWhiteSpace(dto.RouteGeometry)) t.RouteGeometry = route.RouteGeometry;
            if (!dto.CorridorEnabled.HasValue)
            {
                t.CorridorEnabled = route.CorridorEnabled;
                t.CorridorBufferMeters = route.CorridorBufferMeters;
                t.DeviationThresholdMinutes = route.DeviationThresholdMinutes;
            }
            // Replace inherited links with the new route's; keep direct-only links
            // (geofences the user attached to this trip explicitly).
            var keepDirect = t.TripGeofences
                .Where(g => !g.IsDeleted && route.RouteGeofences.All(rg => rg.GeofenceId != g.GeofenceId))
                .ToList();
            foreach (var g in t.TripGeofences.ToList()) _db.TripGeofences.Remove(g);
            foreach (var rg in route.RouteGeofences.Where(rg => !rg.IsDeleted))
            {
                _db.TripGeofences.Add(new TripGeofence
                {
                    Id = Guid.NewGuid(), TripId = t.Id, TenantId = t.CompanyId,
                    GeofenceId = rg.GeofenceId, Role = (TripGeofenceRole)rg.Role, SequenceOrder = rg.SequenceOrder
                });
            }
            foreach (var g in keepDirect)
            {
                _db.TripGeofences.Add(new TripGeofence
                {
                    Id = Guid.NewGuid(), TripId = t.Id, TenantId = t.CompanyId,
                    GeofenceId = g.GeofenceId, Role = g.Role, SequenceOrder = g.SequenceOrder,
                    Visited = g.Visited, VisitedAt = g.VisitedAt
                });
            }
        }
        else if (dto.RouteId == Guid.Empty && t.RouteId.HasValue)
        {
            // Explicit unlink: drop route-inherited links? No — keep the trip's
            // current links (they become direct links). Just clear the reference.
            t.RouteId = null;
        }

        // Waypoint replace-all (the ordered list the user edited).
        if (dto.Waypoints != null)
        {
            var waypoints = BuildWaypoints(dto.Waypoints);
            var errors = TripLifecycleService.ValidateWaypoints(waypoints, t.Type);
            if (errors.Count > 0)
                return BadRequest(new ApiResponse<object> { Success = false, Message = string.Join(" ", errors) });
            var old = await _db.TripWaypoints.Where(w => w.TripId == t.Id && !w.IsDeleted).ToListAsync();
            _db.TripWaypoints.RemoveRange(old);
            foreach (var w in waypoints) { w.Id = Guid.NewGuid(); w.TripId = t.Id; w.TenantId = t.CompanyId; _db.TripWaypoints.Add(w); }
            t.StartLocation = waypoints[0].Name;
            t.StartLatitude = waypoints[0].Latitude;
            t.StartLongitude = waypoints[0].Longitude;
            t.EndLocation = waypoints[^1].Name;
            t.EndLatitude = waypoints[^1].Latitude;
            t.EndLongitude = waypoints[^1].Longitude;
        }

        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<object> { Success = true, Message = "Trip updated." });
    }

    // ── Status transitions ─────────────────────────────────────────────────

    [HttpPost("{id:guid}/status")]
    [RequirePermission("trip.update")]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] UpdateTripStatusDto dto)
    {
        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var t = await _db.Trips
            .Include(t => t.TripWaypoints)
            .Include(t => t.TripGeofences)
            .Include(t => t.Route).ThenInclude(r => r!.RouteGeofences)
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted && (isSuperAdmin || t.CompanyId == tenantId));
        if (t == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Trip not found." });

        var source = string.IsNullOrWhiteSpace(dto.Source) ? "manual" : dto.Source!;
        var errors = await _lifecycle.TransitionAsync(t, (TripStatus)dto.Status, dto.Reason, source, GetUserId().ToString());
        if (errors.Count > 0)
            return BadRequest(new ApiResponse<object> { Success = false, Message = string.Join(" ", errors) });

        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<object>
        {
            Success = true,
            Message = $"Trip is now {t.Status}.",
            Data = new { t.Id, Status = (int)t.Status, StatusName = t.Status.ToString(), t.IsDelayed, t.DelayReason, t.ActualStartTime, t.ActualEndTime, t.ActualDistance, t.MaxSpeed, t.AverageSpeed, t.FuelUsedLiters, t.IdleMinutes }
        });
    }

    /// <summary>Manual waypoint arrival (also the hook a geofence/telemetry event pipeline would call).</summary>
    [HttpPost("{id:guid}/waypoints/{waypointId:guid}/arrive")]
    [RequirePermission("trip.update")]
    public async Task<IActionResult> ArriveAtWaypoint(Guid id, Guid waypointId, [FromBody] ArriveWaypointDto? dto)
    {
        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var t = await _db.Trips.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted && (isSuperAdmin || t.CompanyId == tenantId));
        if (t == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Trip not found." });

        var ok = await _lifecycle.RecordWaypointArrivalAsync(id, waypointId, dto?.ArrivedAt);
        if (!ok) return NotFound(new ApiResponse<object> { Success = false, Message = "Waypoint not found on this trip." });
        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<object> { Success = true, Message = "Waypoint arrival recorded." });
    }

    // ── Zone events (geofence/telemetry pipeline → auto lifecycle) ────────

    /// <summary>
    /// Entry point for the geofence/telemetry event pipeline (and for a
    /// dispatcher simulating one): exit of the origin zone auto-starts a
    /// scheduled trip, entry of the end zone auto-completes an in-progress one,
    /// checkpoint entries mark visits, restricted-zone entries raise alerts.
    /// Same tenant isolation as every other trip endpoint — the caller must
    /// own the trip (or be SuperAdmin).
    /// </summary>
    [HttpPost("{id:guid}/zone-events")]
    [RequirePermission("trip.update")]
    public async Task<IActionResult> PostZoneEvent(Guid id, [FromBody] TripZoneEventDto dto)
    {
        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var owned = await _db.Trips.AsNoTracking()
            .AnyAsync(t => t.Id == id && !t.IsDeleted && (isSuperAdmin || t.CompanyId == tenantId));
        if (!owned) return NotFound(new ApiResponse<object> { Success = false, Message = "Trip not found." });

        var result = await _lifecycle.HandleZoneEventAsync(id, dto.GeofenceId, (TripZoneEventKind)dto.Kind, dto.At ?? DateTime.UtcNow);
        await _db.SaveChangesAsync();
        if (result.Error != null)
            return BadRequest(new ApiResponse<object> { Success = false, Message = result.Error });

        return Ok(new ApiResponse<object>
        {
            Success = true,
            Data = new
            {
                result.StatusChanged,
                NewStatus = result.NewStatus.HasValue ? (int)result.NewStatus.Value : (int?)null,
                result.Warnings
            }
        });
    }

    // ── Geofence linkage (checkpoints / restricted zones) ─────────────────

    [HttpGet("{id:guid}/geofences")]
    [RequirePermission("trip.view")]
    public async Task<IActionResult> GetTripGeofences(Guid id)
    {
        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var exists = await _db.Trips.AsNoTracking()
            .AnyAsync(t => t.Id == id && !t.IsDeleted && (isSuperAdmin || t.CompanyId == tenantId));
        if (!exists) return NotFound(new ApiResponse<object> { Success = false, Message = "Trip not found." });

        var rows = await _db.TripGeofences.AsNoTracking()
            .Where(g => g.TripId == id && !g.IsDeleted && (isSuperAdmin || g.Trip.CompanyId == tenantId))
            .OrderBy(g => g.Role == TripGeofenceRole.Checkpoint ? 0 : 1)
            .ThenBy(g => g.SequenceOrder)
            .Select(g => new TripGeofenceDto
            {
                Id = g.Id, TripId = g.TripId, GeofenceId = g.GeofenceId,
                GeofenceName = g.Geofence.Name,
                GeofenceType = (int)g.Geofence.Type, GeofenceTypeName = g.Geofence.Type.ToString(),
                Geometry = g.Geofence.Geometry,
                CenterLatitude = g.Geofence.CenterLatitude, CenterLongitude = g.Geofence.CenterLongitude, Radius = g.Geofence.Radius,
                Role = (int)g.Role, RoleName = g.Role.ToString(),
                SequenceOrder = g.SequenceOrder,
                Visited = g.Visited, VisitedAt = g.VisitedAt
            }).ToListAsync();

        return Ok(new ApiResponse<List<TripGeofenceDto>> { Success = true, Data = rows });
    }

    /// <summary>Replace-all semantics, mirroring the route geofence endpoint.</summary>
    [HttpPut("{id:guid}/geofences")]
    [RequirePermission("trip.update")]
    public async Task<IActionResult> ReplaceTripGeofences(Guid id, [FromBody] List<TripGeofenceLinkDto> links)
    {
        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var t = await _db.Trips.FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted && (isSuperAdmin || t.CompanyId == tenantId));
        if (t == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Trip not found." });

        var linkErrors = TripLifecycleService.ValidateGeofenceLinks(
            links.Select(l => (l.GeofenceId, l.Role, l.SequenceOrder)).ToList());
        if (linkErrors.Count > 0)
            return BadRequest(new ApiResponse<object> { Success = false, Message = string.Join(" ", linkErrors) });

        var geofenceIds = links.Select(l => l.GeofenceId).ToList();
        var valid = await _db.Geofences.AsNoTracking()
            .Where(g => geofenceIds.Contains(g.Id) && !g.IsDeleted)
            .Select(g => new { g.Id, g.CompanyId })
            .ToListAsync();
        var byId = valid.ToDictionary(v => v.Id);
        foreach (var l in links)
        {
            if (!byId.TryGetValue(l.GeofenceId, out var gf) || gf.CompanyId != t.CompanyId)
                return BadRequest(new ApiResponse<object> { Success = false, Message = "Every linked geofence must belong to the trip's company." });
        }

        var old = await _db.TripGeofences.Where(g => g.TripId == id && !g.IsDeleted).ToListAsync();
        _db.TripGeofences.RemoveRange(old);
        foreach (var l in links)
        {
            _db.TripGeofences.Add(new TripGeofence
            {
                Id = Guid.NewGuid(), TripId = id, TenantId = t.CompanyId,
                GeofenceId = l.GeofenceId, Role = (TripGeofenceRole)l.Role, SequenceOrder = l.SequenceOrder
            });
        }
        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<object> { Success = true, Message = $"Trip geofence links updated ({links.Count} linked)." });
    }

    // ── Live position + replay (telemetry stream, no parallel ingestion) ──

    [HttpGet("{id:guid}/live")]
    [RequirePermission("trip.view")]
    public async Task<IActionResult> GetLivePosition(Guid id)
    {
        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var t = await _db.Trips.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted && (isSuperAdmin || t.CompanyId == tenantId));
        if (t == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Trip not found." });

        var state = await _db.TelemetryStates.AsNoTracking()
            .FirstOrDefaultAsync(s => s.VehicleId == t.VehicleId);
        if (state == null)
            return Ok(new ApiResponse<TripLivePositionDto> { Success = true, Data = new TripLivePositionDto() });

        return Ok(new ApiResponse<TripLivePositionDto>
        {
            Success = true,
            Data = new TripLivePositionDto
            {
                Latitude = state.Latitude, Longitude = state.Longitude,
                SpeedKmh = state.SpeedKmh, HeadingDeg = state.HeadingDeg,
                UpdatedAt = state.UpdatedAt
            }
        });
    }

    [HttpGet("{id:guid}/replay")]
    [RequirePermission("trip.view")]
    public async Task<IActionResult> GetReplay(Guid id)
    {
        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var t = await _db.Trips.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted && (isSuperAdmin || t.CompanyId == tenantId));
        if (t == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Trip not found." });

        if (t.ActualStartTime == null)
            return Ok(new ApiResponse<List<TripReplayPointDto>> { Success = true, Data = new List<TripReplayPointDto>() });

        var start = t.ActualStartTime.Value;
        var end = t.ActualEndTime ?? DateTime.UtcNow;
        var points = await _db.TelemetryEvents.AsNoTracking()
            .Where(e => e.VehicleId == t.VehicleId && e.EventTimeUtc >= start && e.EventTimeUtc <= end)
            .OrderBy(e => e.EventTimeUtc)
            .Select(e => new TripReplayPointDto
            {
                EventTimeUtc = e.EventTimeUtc,
                Latitude = e.Latitude, Longitude = e.Longitude,
                SpeedKmh = e.SpeedKmh, HeadingDeg = e.HeadingDeg,
                Ignition = e.Ignition
            })
            .ToListAsync();

        return Ok(new ApiResponse<List<TripReplayPointDto>> { Success = true, Data = points });
    }

    // ── Delete ──────────────────────────────────────────────────────────────

    [HttpDelete("{id:guid}")]
    [RequirePermission("trip.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var t = await _db.Trips.FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted && (isSuperAdmin || t.CompanyId == tenantId));
        if (t == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Trip not found." });
        if (t.Status == TripStatus.InProgress)
            return BadRequest(new ApiResponse<object> { Success = false, Message = "An in-progress trip cannot be deleted — cancel or abort it first." });

        t.IsDeleted = true;
        t.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<object> { Success = true, Message = "Trip deleted." });
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static List<TripWaypoint> BuildWaypoints(List<TripWaypointDto>? dto)
    {
        if (dto == null) return new List<TripWaypoint>();
        return dto
            .OrderBy(w => w.SequenceOrder)
            .Select(w => new TripWaypoint
            {
                Id = Guid.NewGuid(),
                SequenceOrder = w.SequenceOrder,
                LegType = (TripLegType)w.LegType,
                WaypointType = (TripWaypointType)w.WaypointType,
                Name = string.IsNullOrWhiteSpace(w.Name) ? $"Stop {w.SequenceOrder}" : w.Name,
                Latitude = w.Latitude,
                Longitude = w.Longitude,
                Address = w.Address,
                ExpectedArrival = w.ExpectedArrival,
                LinkedGeofenceId = w.LinkedGeofenceId
            }).ToList();
    }

    private static List<TripWaypoint> RouteWaypointsToTrip(FleetRoute route)
    {
        var result = new List<TripWaypoint>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(route.Waypoints ?? "[]");
            var arr = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array ? doc.RootElement : default;
            if (arr.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var seq = 1;
                foreach (var w in arr.EnumerateArray())
                {
                    var lat = w.TryGetProperty("lat", out var latEl) && latEl.ValueKind == System.Text.Json.JsonValueKind.Number ? latEl.GetDouble() : 0;
                    var lng = w.TryGetProperty("lng", out var lngEl) && lngEl.ValueKind == System.Text.Json.JsonValueKind.Number ? lngEl.GetDouble() : 0;
                    var name = w.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                    result.Add(new TripWaypoint
                    {
                        Id = Guid.NewGuid(),
                        SequenceOrder = seq++,
                        LegType = TripLegType.Outbound,
                        WaypointType = TripWaypointType.Other,
                        Name = string.IsNullOrWhiteSpace(name) ? $"Stop {seq - 1}" : name!,
                        Latitude = lat,
                        Longitude = lng
                    });
                }
            }
        }
        catch (System.Text.Json.JsonException) { /* no waypoints — caller validates */ }
        if (result.Count == 0 && !string.IsNullOrWhiteSpace(route.OriginName))
        {
            result.Add(new TripWaypoint { Id = Guid.NewGuid(), SequenceOrder = 1, Name = route.OriginName, Latitude = route.OriginLatitude, Longitude = route.OriginLongitude });
            if (!string.IsNullOrWhiteSpace(route.DestinationName) && route.DestinationLatitude.HasValue && route.DestinationLongitude.HasValue)
                result.Add(new TripWaypoint { Id = Guid.NewGuid(), SequenceOrder = 2, Name = route.DestinationName!, Latitude = route.DestinationLatitude.Value, Longitude = route.DestinationLongitude.Value });
        }
        return result;
    }

    private static TripDto ToDto(Trip t)
    {
        return new TripDto
        {
            Id = t.Id, Name = t.Name, Description = t.Description,
            Status = (int)t.Status, StatusName = t.Status.ToString(),
            IsDelayed = t.IsDelayed, DelayReason = t.DelayReason, CancelReason = t.CancelReason,
            Type = (int)t.Type, TypeName = t.Type.ToString(),
            CompanyName = t.Company.Name,
            VehicleId = t.VehicleId, VehicleName = t.Vehicle.RegistrationNumber,
            DriverId = t.DriverId, DriverName = t.Driver.FirstName + " " + t.Driver.LastName,
            RouteId = t.RouteId, RouteName = t.Route?.Name,
            ScheduledStartTime = t.ScheduledStartTime, ScheduledEndTime = t.ScheduledEndTime,
            ActualStartTime = t.ActualStartTime, ActualEndTime = t.ActualEndTime,
            PlannedDistance = t.PlannedDistance, ActualDistance = t.ActualDistance,
            PlannedDuration = t.PlannedDuration, ActualDuration = t.ActualDuration,
            MaxSpeed = t.MaxSpeed, AverageSpeed = t.AverageSpeed,
            FuelUsedLiters = t.FuelUsedLiters, IdleMinutes = t.IdleMinutes,
            RouteGeometry = t.RouteGeometry,
            CorridorEnabled = t.CorridorEnabled, CorridorBufferMeters = t.CorridorBufferMeters,
            DeviationThresholdMinutes = t.DeviationThresholdMinutes,
            WaypointCount = t.TripWaypoints.Count,
            GeofenceCount = t.TripGeofences.Count,
            CheckpointCount = t.TripGeofences.Count(x => x.Role == TripGeofenceRole.Checkpoint),
            RestrictedZoneCount = t.TripGeofences.Count(x => x.Role == TripGeofenceRole.RestrictedZone),
            BoundaryZoneCount = t.TripGeofences.Count(x => x.Role == TripGeofenceRole.StartZone || x.Role == TripGeofenceRole.EndZone),
            Waypoints = t.TripWaypoints.OrderBy(w => w.SequenceOrder).Select(w => new TripWaypointDto
            {
                Id = w.Id, SequenceOrder = w.SequenceOrder,
                LegType = (int)w.LegType, LegTypeName = w.LegType.ToString(),
                WaypointType = (int)w.WaypointType, WaypointTypeName = w.WaypointType.ToString(),
                Name = w.Name, Latitude = w.Latitude, Longitude = w.Longitude,
                Address = w.Address, ExpectedArrival = w.ExpectedArrival, ActualArrival = w.ActualArrival,
                LinkedGeofenceId = w.LinkedGeofenceId
            }).ToList(),
            TripGeofences = t.TripGeofences.Select(g => new TripGeofenceDto
            {
                Id = g.Id, TripId = g.TripId, GeofenceId = g.GeofenceId,
                GeofenceName = g.Geofence.Name,
                GeofenceType = (int)g.Geofence.Type, GeofenceTypeName = g.Geofence.Type.ToString(),
                Geometry = g.Geofence.Geometry,
                CenterLatitude = g.Geofence.CenterLatitude, CenterLongitude = g.Geofence.CenterLongitude, Radius = g.Geofence.Radius,
                Role = (int)g.Role, RoleName = g.Role.ToString(),
                SequenceOrder = g.SequenceOrder,
                Visited = g.Visited, VisitedAt = g.VisitedAt
            }).ToList(),
            StatusHistory = t.StatusHistory.OrderBy(h => h.ChangedAt).Select(h => new TripStatusHistoryDto
            {
                FromStatus = (int)h.FromStatus, ToStatus = (int)h.ToStatus,
                Reason = h.Reason, Source = h.Source, ChangedAt = h.ChangedAt
            }).ToList(),
            CreatedAt = t.CreatedAt
        };
    }
}

public class ArriveWaypointDto
{
    public DateTime? ArrivedAt { get; set; }
}