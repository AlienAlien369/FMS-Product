using System.Security.Claims;
using Freebuff.Platform.Api.Authorization;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Shared.Models;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class GeofencesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public GeofencesController(ApplicationDbContext db) => _db = db;

    private Guid GetTenantId() => Guid.Parse(User.FindFirstValue("tenant_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsSuperAdmin() => User.IsInRole("SuperAdmin") || User.Claims.Any(c => c.Type == "is_super_admin" && c.Value == "true");
    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    [RequirePermission("geofence.view")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] int? status = null, [FromQuery] int? type = null,
        [FromQuery] string? sortBy = null, [FromQuery] bool sortDesc = false)
    {
        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();

        var query = _db.Geofences.AsNoTracking()
            .Where(g => !g.IsDeleted && (isSuperAdmin || g.CompanyId == tenantId))
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(g => g.Name.Contains(search) || (g.Description != null && g.Description.Contains(search)));
        if (status.HasValue) query = query.Where(g => (int)g.Status == status.Value);
        if (type.HasValue) query = query.Where(g => (int)g.Type == type.Value);

        // Server-side sorting
        query = sortBy?.ToLower() switch
        {
            "name" => sortDesc ? query.OrderByDescending(g => g.Name) : query.OrderBy(g => g.Name),
            "type" => sortDesc ? query.OrderByDescending(g => g.Type) : query.OrderBy(g => g.Type),
            "status" => sortDesc ? query.OrderByDescending(g => g.Status) : query.OrderBy(g => g.Status),
            _ => query.OrderByDescending(g => g.CreatedAt)
        };

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(g => new GeofenceDto
            {
                Id = g.Id, Name = g.Name, Description = g.Description,
                Type = (int)g.Type, TypeName = g.Type.ToString(),
                Status = (int)g.Status,
                CompanyName = g.Company.Name,
                Coordinates = g.Coordinates, CenterLatitude = g.CenterLatitude, CenterLongitude = g.CenterLongitude, Radius = g.Radius,
                FillColor = g.FillColor, BorderColor = g.BorderColor, BorderWidth = g.BorderWidth,
                AlertOnEntry = g.VehicleGeofences.Any() ? g.VehicleGeofences.First().AlertOnEntry : true,
                AlertOnExit = g.VehicleGeofences.Any() ? g.VehicleGeofences.First().AlertOnExit : true,
                AlertOnDwell = g.VehicleGeofences.Any() ? g.VehicleGeofences.First().AlertOnDwell : false,
                DwellTimeMinutes = g.VehicleGeofences.Any() ? g.VehicleGeofences.First().DwellTimeMinutes : null,
                AssignedVehicleCount = g.VehicleGeofences.Count,
                ViolationCount = g.ViolationCount,
                CreatedAt = g.CreatedAt
            }).ToListAsync();

        return Ok(new ApiResponse<PagedResult<GeofenceDto>>
        {
            Success = true,
            Data = new PagedResult<GeofenceDto> { Items = items, TotalCount = total, Page = page, PageSize = pageSize }
        });
    }

    [HttpGet("stats")]
    [RequirePermission("geofence.view")]
    public async Task<IActionResult> GetStats()
    {
        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var query = _db.Geofences.AsNoTracking().Where(g => !g.IsDeleted && (isSuperAdmin || g.CompanyId == tenantId));

        return Ok(new ApiResponse<object>
        {
            Success = true,
            Data = new
            {
                Total = await query.CountAsync(),
                Active = await query.CountAsync(g => g.Status == EntityStatus.Active),
                Inactive = await query.CountAsync(g => g.Status == EntityStatus.Inactive),
                Circles = await query.CountAsync(g => g.Type == GeofenceType.Circle),
                Rectangles = await query.CountAsync(g => g.Type == GeofenceType.Rectangle),
                Polygons = await query.CountAsync(g => g.Type == GeofenceType.Polygon),
                TotalAssignments = await _db.VehicleGeofences.AsNoTracking()
                    .Where(vg => !vg.IsDeleted && (isSuperAdmin || vg.Geofence.CompanyId == tenantId)).CountAsync(),
                TotalViolations = await query.SumAsync(g => g.ViolationCount)
            }
        });
    }

    [HttpGet("{id:guid}")]
    [RequirePermission("geofence.view")]
    public async Task<IActionResult> Get(Guid id)
    {
        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var g = await _db.Geofences.AsNoTracking()
            .Include(g => g.Company).Include(g => g.VehicleGeofences).ThenInclude(vg => vg.Vehicle)
            .Include(g => g.VehicleGeofences).ThenInclude(vg => vg.Driver)
            .FirstOrDefaultAsync(g => g.Id == id && !g.IsDeleted && (isSuperAdmin || g.CompanyId == tenantId));

        if (g == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Geofence not found." });

        var vg = g.VehicleGeofences.FirstOrDefault();
        return Ok(new ApiResponse<GeofenceDto>
        {
            Success = true,
            Data = new GeofenceDto
            {
                Id = g.Id, Name = g.Name, Description = g.Description,
                Type = (int)g.Type, TypeName = g.Type.ToString(),
                Status = (int)g.Status,
                CompanyName = g.Company.Name,
                Coordinates = g.Coordinates, CenterLatitude = g.CenterLatitude, CenterLongitude = g.CenterLongitude, Radius = g.Radius,
                FillColor = g.FillColor, BorderColor = g.BorderColor, BorderWidth = g.BorderWidth,
                AlertOnEntry = vg?.AlertOnEntry ?? true, AlertOnExit = vg?.AlertOnExit ?? true,
                AlertOnDwell = vg?.AlertOnDwell ?? false, DwellTimeMinutes = vg?.DwellTimeMinutes,
                AssignedVehicleCount = g.VehicleGeofences.Count,
                ViolationCount = g.ViolationCount,
                CreatedAt = g.CreatedAt
            }
        });
    }

    [HttpPost]
    [RequirePermission("geofence.create")]
    public async Task<IActionResult> Create([FromBody] CreateGeofenceDto dto)
    {

        var tenantId = GetTenantId();
        var g = new Geofence
        {
            Id = Guid.NewGuid(), Name = dto.Name, Description = dto.Description,
            Type = (GeofenceType)dto.Type, Status = EntityStatus.Active,
            Coordinates = dto.Coordinates, CenterLatitude = dto.CenterLatitude, CenterLongitude = dto.CenterLongitude, Radius = dto.Radius,
            FillColor = dto.FillColor, BorderColor = dto.BorderColor, BorderWidth = dto.BorderWidth,
            CompanyId = tenantId, TenantId = tenantId
        };
        _db.Geofences.Add(g);

        if (dto.AssignedVehicleIds != null && dto.AssignedVehicleIds.Any())
        {
            // Only assign vehicles that belong to this company
            var validVehicleIds = await _db.Vehicles
                .Where(v => dto.AssignedVehicleIds.Contains(v.Id) && !v.IsDeleted && v.CompanyId == tenantId)
                .Select(v => v.Id)
                .ToListAsync();
            foreach (var vid in validVehicleIds)
            {
                _db.VehicleGeofences.Add(new VehicleGeofence
                {
                    Id = Guid.NewGuid(), GeofenceId = g.Id, VehicleId = vid,
                    AlertOnEntry = dto.AlertOnEntry, AlertOnExit = dto.AlertOnExit,
                    AlertOnDwell = dto.AlertOnDwell, DwellTimeMinutes = dto.DwellTimeMinutes,
                    TenantId = tenantId
                });
            }
        }

        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = g.Id }, new ApiResponse<object> { Success = true, Message = "Geofence created." });
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("geofence.update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateGeofenceDto dto)
    {

        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var g = await _db.Geofences.FirstOrDefaultAsync(g => g.Id == id && !g.IsDeleted && (isSuperAdmin || g.CompanyId == tenantId));
        if (g == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Geofence not found." });

        if (dto.Name != null) g.Name = dto.Name;
        if (dto.Description != null) g.Description = dto.Description;
        if (dto.Status.HasValue) g.Status = (EntityStatus)dto.Status.Value;
        if (dto.Type.HasValue) g.Type = (GeofenceType)dto.Type.Value;
        if (dto.Coordinates != null) g.Coordinates = dto.Coordinates;
        if (dto.CenterLatitude.HasValue) g.CenterLatitude = dto.CenterLatitude.Value;
        if (dto.CenterLongitude.HasValue) g.CenterLongitude = dto.CenterLongitude.Value;
        if (dto.Radius.HasValue) g.Radius = dto.Radius.Value;
        if (dto.FillColor != null) g.FillColor = dto.FillColor;
        if (dto.BorderColor != null) g.BorderColor = dto.BorderColor;
        if (dto.BorderWidth.HasValue) g.BorderWidth = dto.BorderWidth.Value;

        var vgs = await _db.VehicleGeofences.Where(vg => vg.GeofenceId == id && !vg.IsDeleted).ToListAsync();
        foreach (var vg in vgs)
        {
            if (dto.AlertOnEntry.HasValue) vg.AlertOnEntry = dto.AlertOnEntry.Value;
            if (dto.AlertOnExit.HasValue) vg.AlertOnExit = dto.AlertOnExit.Value;
            if (dto.AlertOnDwell.HasValue) vg.AlertOnDwell = dto.AlertOnDwell.Value;
            if (dto.DwellTimeMinutes.HasValue) vg.DwellTimeMinutes = dto.DwellTimeMinutes.Value;
        }

        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<object> { Success = true, Message = "Geofence updated." });
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("geofence.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {

        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var g = await _db.Geofences.FirstOrDefaultAsync(g => g.Id == id && !g.IsDeleted && (isSuperAdmin || g.CompanyId == tenantId));
        if (g == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Geofence not found." });

        g.IsDeleted = true;
        g.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<object> { Success = true, Message = "Geofence deleted." });
    }

    [HttpPost("{id:guid}/restore")]
    [RequirePermission("geofence.update")]
    public async Task<IActionResult> Restore(Guid id)
    {

        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var g = await _db.Geofences.FirstOrDefaultAsync(g => g.Id == id && g.IsDeleted && (isSuperAdmin || g.CompanyId == tenantId));
        if (g == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Geofence not found." });

        g.IsDeleted = false;
        g.DeletedAt = null;
        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<object> { Success = true, Message = "Geofence restored." });
    }

    [HttpPost("{id:guid}/assign-vehicle")]
    [RequirePermission("geofence.update")]
    public async Task<IActionResult> AssignVehicle(Guid id, [FromBody] AssignVehicleGeofenceDto dto)
    {

        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var g = await _db.Geofences.FirstOrDefaultAsync(g => g.Id == id && !g.IsDeleted && (isSuperAdmin || g.CompanyId == tenantId));
        if (g == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Geofence not found." });

        // Validate vehicle belongs to the same company
        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == dto.VehicleId && !v.IsDeleted && v.CompanyId == tenantId);
        if (vehicle == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Vehicle not found in your company." });

        var exists = await _db.VehicleGeofences.AnyAsync(vg => vg.GeofenceId == id && vg.VehicleId == dto.VehicleId && !vg.IsDeleted);
        if (exists) return BadRequest(new ApiResponse<object> { Success = false, Message = "Vehicle already assigned to this geofence." });

        _db.VehicleGeofences.Add(new VehicleGeofence
        {
            Id = Guid.NewGuid(), GeofenceId = id, VehicleId = dto.VehicleId,
            DriverId = dto.DriverId,
            AlertOnEntry = dto.AlertOnEntry ?? true, AlertOnExit = dto.AlertOnExit ?? true,
            AlertOnDwell = dto.AlertOnDwell ?? false, DwellTimeMinutes = dto.DwellTimeMinutes,
            TenantId = tenantId
        });
        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<object> { Success = true, Message = "Vehicle assigned to geofence." });
    }
}

public class AssignVehicleGeofenceDto
{
    public Guid VehicleId { get; set; }
    public Guid? DriverId { get; set; }
    public bool? AlertOnEntry { get; set; }
    public bool? AlertOnExit { get; set; }
    public bool? AlertOnDwell { get; set; }
    public int? DwellTimeMinutes { get; set; }
}
