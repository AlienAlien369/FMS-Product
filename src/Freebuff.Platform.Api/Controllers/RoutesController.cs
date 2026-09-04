using System.Security.Claims;
using Freebuff.Platform.Api.Authorization;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.CompanyScope;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FleetRoute = Freebuff.Platform.Domain.Entities.Route;

namespace Freebuff.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/routes")]
[Authorize]
public class FleetRoutesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    public FleetRoutesController(ApplicationDbContext db, ITenantContext tenant) { _db = db; _tenant = tenant; }

    private Guid GetTenantId() => Guid.Parse(User.FindFirstValue("tenant_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsSuperAdmin() => User.IsInRole("SuperAdmin") || User.Claims.Any(c => c.Type == "is_super_admin" && c.Value == "true");
    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    [RequirePermission("route.view")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] int? status = null, [FromQuery] int? type = null, [FromQuery] int? priority = null,
        [FromQuery] string? sortBy = null, [FromQuery] bool sortDesc = false)
    {
        // Query-side: effective scope = X-Company-Scope ∩ permitted set (list view).
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var query = _db.Routes.AsNoTracking()
            .Where(r => !r.IsDeleted && (scope == null || scope.Contains(r.CompanyId)))
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(r => r.Name.Contains(search) || (r.Description != null && r.Description.Contains(search)) || r.OriginName.Contains(search) || (r.DestinationName != null && r.DestinationName.Contains(search)));
        if (status.HasValue) query = query.Where(r => (int)r.Status == status.Value);
        if (type.HasValue) query = query.Where(r => (int)r.Type == type.Value);
        if (priority.HasValue) query = query.Where(r => r.Priority == priority.Value);

        // Server-side sorting
        query = sortBy?.ToLower() switch
        {
            "name" => sortDesc ? query.OrderByDescending(r => r.Name) : query.OrderBy(r => r.Name),
            "totaldistance" => sortDesc ? query.OrderByDescending(r => r.TotalDistance) : query.OrderBy(r => r.TotalDistance),
            "priority" => sortDesc ? query.OrderByDescending(r => r.Priority) : query.OrderBy(r => r.Priority),
            "status" => sortDesc ? query.OrderByDescending(r => r.Status) : query.OrderBy(r => r.Status),
            "type" => sortDesc ? query.OrderByDescending(r => r.Type) : query.OrderBy(r => r.Type),
            _ => query.OrderByDescending(r => r.CreatedAt)
        };

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new RouteDto
            {
                Id = r.Id, Name = r.Name, Description = r.Description,
                Status = (int)r.Status, StatusName = r.Status.ToString(),
                Type = (int)r.Type, TypeName = r.Type.ToString(),
                IsOptimized = r.IsOptimized, IsTemplate = r.IsTemplate,
                CompanyName = r.Company.Name,
                OriginName = r.OriginName, OriginLatitude = r.OriginLatitude, OriginLongitude = r.OriginLongitude,
                DestinationName = r.DestinationName, DestinationLatitude = r.DestinationLatitude, DestinationLongitude = r.DestinationLongitude,
                Waypoints = r.Waypoints, RouteGeometry = r.RouteGeometry,
                TotalDistance = r.TotalDistance, DistanceUnit = r.DistanceUnit,
                EstimatedDuration = r.EstimatedDuration,
                EstimatedFuelCost = r.EstimatedFuelCost, EstimatedTollCost = r.EstimatedTollCost,
                Currency = r.Currency, TrafficLevel = r.TrafficLevel,
                ValidFrom = r.ValidFrom, ValidUntil = r.ValidUntil, MaxVehicles = r.MaxVehicles, Priority = r.Priority,
                RecurrenceRule = r.RecurrenceRule, DayOfWeek = r.DayOfWeek, PreferredStartTime = r.PreferredStartTime,
                AssignedVehicleCount = r.RouteVehicles.Count,
                CompletedTripCount = r.Trips.Count(t => t.Status == TripStatus.Completed),
                CreatedAt = r.CreatedAt
            }).ToListAsync();

        return Ok(new ApiResponse<PagedResult<RouteDto>>
        {
            Success = true,
            Data = new PagedResult<RouteDto> { Items = items, TotalCount = total, Page = page, PageSize = pageSize }
        });
    }

    [HttpGet("stats")]
    [RequirePermission("route.view")]
    public async Task<IActionResult> GetStats()
    {
        // Query-side: effective scope = X-Company-Scope ∩ permitted set.
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var query = _db.Routes.AsNoTracking().Where(r => !r.IsDeleted && (scope == null || scope.Contains(r.CompanyId)));

        return Ok(new ApiResponse<object>
        {
            Success = true,
            Data = new
            {
                Total = await query.CountAsync(),
                Active = await query.CountAsync(r => r.Status == RouteStatus.Active),
                Draft = await query.CountAsync(r => r.Status == RouteStatus.Draft),
                InProgress = await query.CountAsync(r => r.Status == RouteStatus.InProgress),
                Completed = await query.CountAsync(r => r.Status == RouteStatus.Completed),
                Templates = await query.CountAsync(r => r.IsTemplate),
                Optimized = await query.CountAsync(r => r.IsOptimized),
                TotalDistance = await query.SumAsync(r => r.TotalDistance ?? 0)
            }
        });
    }

    [HttpGet("{id:guid}")]
    [RequirePermission("route.view")]
    public async Task<IActionResult> Get(Guid id)
    {
        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var r = await _db.Routes.AsNoTracking()
            .Include(r => r.Company).Include(r => r.RouteVehicles).ThenInclude(rv => rv.Vehicle)
            .Include(r => r.RouteVehicles).ThenInclude(rv => rv.Driver)
            .Include(r => r.Trips)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted && (isSuperAdmin || r.CompanyId == tenantId));

        if (r == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Route not found." });

        return Ok(new ApiResponse<RouteDto>
        {
            Success = true,
            Data = new RouteDto
            {
                Id = r.Id, Name = r.Name, Description = r.Description,
                Status = (int)r.Status, StatusName = r.Status.ToString(),
                Type = (int)r.Type, TypeName = r.Type.ToString(),
                IsOptimized = r.IsOptimized, IsTemplate = r.IsTemplate,
                CompanyName = r.Company.Name,
                OriginName = r.OriginName, OriginLatitude = r.OriginLatitude, OriginLongitude = r.OriginLongitude,
                DestinationName = r.DestinationName, DestinationLatitude = r.DestinationLatitude, DestinationLongitude = r.DestinationLongitude,
                Waypoints = r.Waypoints, RouteGeometry = r.RouteGeometry,
                TotalDistance = r.TotalDistance, DistanceUnit = r.DistanceUnit,
                EstimatedDuration = r.EstimatedDuration,
                EstimatedFuelCost = r.EstimatedFuelCost, EstimatedTollCost = r.EstimatedTollCost,
                Currency = r.Currency, TrafficLevel = r.TrafficLevel,
                ValidFrom = r.ValidFrom, ValidUntil = r.ValidUntil, MaxVehicles = r.MaxVehicles, Priority = r.Priority,
                RecurrenceRule = r.RecurrenceRule, DayOfWeek = r.DayOfWeek, PreferredStartTime = r.PreferredStartTime,
                AssignedVehicleCount = r.RouteVehicles.Count,
                CompletedTripCount = r.Trips.Count(t => t.Status == TripStatus.Completed),
                CreatedAt = r.CreatedAt
            }
        });
    }

    [HttpPost]
    [RequirePermission("route.create")]
    public async Task<IActionResult> Create([FromBody] CreateRouteDto dto)
    {

        var tenantId = GetTenantId();
        var r = new FleetRoute
        {
            Id = Guid.NewGuid(), Name = dto.Name, Description = dto.Description,
            Type = (RouteType)dto.Type, IsTemplate = dto.IsTemplate, Status = RouteStatus.Draft,
            OriginName = dto.OriginName, OriginLatitude = dto.OriginLatitude, OriginLongitude = dto.OriginLongitude,
            DestinationName = dto.DestinationName, DestinationLatitude = dto.DestinationLatitude, DestinationLongitude = dto.DestinationLongitude,
            Waypoints = dto.Waypoints, RouteGeometry = dto.RouteGeometry,
            TotalDistance = dto.TotalDistance, DistanceUnit = dto.DistanceUnit,
            EstimatedDuration = dto.EstimatedDuration,
            EstimatedFuelCost = dto.EstimatedFuelCost, EstimatedTollCost = dto.EstimatedTollCost,
            Currency = dto.Currency, TrafficLevel = dto.TrafficLevel,
            ValidFrom = dto.ValidFrom, ValidUntil = dto.ValidUntil, MaxVehicles = dto.MaxVehicles, Priority = dto.Priority,
            RecurrenceRule = dto.RecurrenceRule, DayOfWeek = dto.DayOfWeek, PreferredStartTime = dto.PreferredStartTime,
            CompanyId = tenantId, TenantId = tenantId
        };
        _db.Routes.Add(r);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = r.Id }, new ApiResponse<RouteDto> { Success = true, Message = "Route created." });
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("route.update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRouteDto dto)
    {

        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var r = await _db.Routes.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted && (isSuperAdmin || r.CompanyId == tenantId));
        if (r == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Route not found." });

        if (dto.Name != null) r.Name = dto.Name;
        if (dto.Description != null) r.Description = dto.Description;
        if (dto.Status.HasValue) r.Status = (RouteStatus)dto.Status.Value;
        if (dto.Type.HasValue) r.Type = (RouteType)dto.Type.Value;
        if (dto.IsOptimized.HasValue) r.IsOptimized = dto.IsOptimized.Value;
        if (dto.IsTemplate.HasValue) r.IsTemplate = dto.IsTemplate.Value;
        if (dto.OriginName != null) r.OriginName = dto.OriginName;
        if (dto.OriginLatitude.HasValue) r.OriginLatitude = dto.OriginLatitude.Value;
        if (dto.OriginLongitude.HasValue) r.OriginLongitude = dto.OriginLongitude.Value;
        if (dto.DestinationName != null) r.DestinationName = dto.DestinationName;
        if (dto.DestinationLatitude.HasValue) r.DestinationLatitude = dto.DestinationLatitude.Value;
        if (dto.DestinationLongitude.HasValue) r.DestinationLongitude = dto.DestinationLongitude.Value;
        if (dto.Waypoints != null) r.Waypoints = dto.Waypoints;
        if (dto.RouteGeometry != null) r.RouteGeometry = dto.RouteGeometry;
        if (dto.TotalDistance.HasValue) r.TotalDistance = dto.TotalDistance.Value;
        if (dto.EstimatedDuration.HasValue) r.EstimatedDuration = dto.EstimatedDuration.Value;
        if (dto.EstimatedFuelCost.HasValue) r.EstimatedFuelCost = dto.EstimatedFuelCost.Value;
        if (dto.EstimatedTollCost.HasValue) r.EstimatedTollCost = dto.EstimatedTollCost.Value;
        if (dto.TrafficLevel.HasValue) r.TrafficLevel = dto.TrafficLevel.Value;
        if (dto.ValidFrom.HasValue) r.ValidFrom = dto.ValidFrom.Value;
        if (dto.ValidUntil.HasValue) r.ValidUntil = dto.ValidUntil.Value;
        if (dto.MaxVehicles.HasValue) r.MaxVehicles = dto.MaxVehicles.Value;
        if (dto.Priority.HasValue) r.Priority = dto.Priority.Value;
        if (dto.RecurrenceRule != null) r.RecurrenceRule = dto.RecurrenceRule;
        if (dto.DayOfWeek.HasValue) r.DayOfWeek = dto.DayOfWeek.Value;
        if (dto.PreferredStartTime.HasValue) r.PreferredStartTime = dto.PreferredStartTime.Value;

        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<object> { Success = true, Message = "Route updated." });
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("route.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {

        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var r = await _db.Routes.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted && (isSuperAdmin || r.CompanyId == tenantId));
        if (r == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Route not found." });

        r.IsDeleted = true;
        r.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<object> { Success = true, Message = "Route deleted." });
    }

    [HttpPost("{id:guid}/restore")]
    [RequirePermission("route.update")]
    public async Task<IActionResult> Restore(Guid id)
    {

        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var r = await _db.Routes.FirstOrDefaultAsync(r => r.Id == id && r.IsDeleted && (isSuperAdmin || r.CompanyId == tenantId));
        if (r == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Route not found." });

        r.IsDeleted = false;
        r.DeletedAt = null;
        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<object> { Success = true, Message = "Route restored." });
    }
}
