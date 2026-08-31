using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Api.Fleet.Data;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Fleet.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class VehiclesController : ControllerBase
{
    private readonly FleetDbContext _db;
    public VehiclesController(FleetDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<VehicleDto>>>> GetAll([FromQuery] PagedRequest filter)
    {
        var tenantId = User.FindFirst("tenant_id")?.Value;
        var query = _db.Vehicles.AsNoTracking().Include(v => v.Driver).Where(v => !v.IsDeleted).AsQueryable();
        if (Guid.TryParse(tenantId, out var tid)) query = query.Where(v => v.CompanyId == tid);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(v => v.RegistrationNumber.Contains(filter.Search) || (v.Name != null && v.Name.Contains(filter.Search)));

        var totalCount = await query.CountAsync();
        var items = await query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(v => new VehicleDto
            {
                Id = v.Id, RegistrationNumber = v.RegistrationNumber, Name = v.Name, VehicleType = v.VehicleType,
                Make = v.Make, Model = v.Model, Year = v.Year, FuelType = (int)v.FuelType, CompanyId = v.CompanyId,
                DriverId = v.DriverId, DriverName = v.Driver != null ? v.Driver.FirstName + " " + v.Driver.LastName : null,
                Status = (int)v.Status, LastLatitude = v.LastLatitude, LastLongitude = v.LastLongitude,
                LastSpeed = v.LastSpeed, LastLocationUpdate = v.LastLocationUpdate, IgnitionStatus = v.IgnitionStatus
            }).ToListAsync();

        return Ok(ApiResponse<PagedResult<VehicleDto>>.Ok(new PagedResult<VehicleDto>
        {
            Items = items, TotalCount = totalCount, Page = filter.Page, PageSize = filter.PageSize
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<VehicleDto>>> GetById(Guid id)
    {
        var tenantId = User.FindFirst("tenant_id")?.Value;
        var query = _db.Vehicles.AsNoTracking().Include(v => v.Driver).Where(v => v.Id == id && !v.IsDeleted);
        if (Guid.TryParse(tenantId, out var tid)) query = query.Where(v => v.CompanyId == tid);

        var v = await query.FirstOrDefaultAsync();
        if (v == null) return NotFound(ApiResponse<VehicleDto>.Fail("NOT_FOUND", "Vehicle not found"));
        return Ok(ApiResponse<VehicleDto>.Ok(new VehicleDto
        {
            Id = v.Id, RegistrationNumber = v.RegistrationNumber, Name = v.Name, Make = v.Make, Model = v.Model,
            Year = v.Year, FuelType = (int)v.FuelType, CompanyId = v.CompanyId, Status = (int)v.Status
        }));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<VehicleDto>>> Create([FromBody] CreateVehicleDto dto)
    {
        var tenantId = User.FindFirst("tenant_id")?.Value ?? throw new UnauthorizedAccessException("No tenant");
        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(), RegistrationNumber = dto.RegistrationNumber, Name = dto.Name,
            VehicleType = dto.VehicleType, Make = dto.Make, Model = dto.Model, Year = dto.Year,
            FuelType = (FuelType)dto.FuelType, FuelTankCapacity = dto.FuelTankCapacity,
            EngineNumber = dto.EngineNumber, ChassisNumber = dto.ChassisNumber, DeviceImei = dto.DeviceImei,
            CompanyId = Guid.Parse(tenantId), Status = VehicleStatus.Active
        };
        _db.Vehicles.Add(vehicle);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = vehicle.Id }, ApiResponse<VehicleDto>.Ok(new VehicleDto
        {
            Id = vehicle.Id, RegistrationNumber = vehicle.RegistrationNumber, Name = vehicle.Name,
            Make = vehicle.Make, Model = vehicle.Model, Year = vehicle.Year,
            FuelType = (int)vehicle.FuelType, CompanyId = vehicle.CompanyId, Status = (int)vehicle.Status
        }));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<VehicleDto>>> Update(Guid id, [FromBody] UpdateVehicleDto dto)
    {
        var vehicle = await _db.Vehicles.FindAsync(id);
        if (vehicle == null || vehicle.IsDeleted) return NotFound(ApiResponse<VehicleDto>.Fail("NOT_FOUND", "Vehicle not found"));

        // Enforce tenant isolation
        var tenantId = User.FindFirst("tenant_id")?.Value;
        if (Guid.TryParse(tenantId, out var tid) && vehicle.CompanyId != tid)
            return NotFound(ApiResponse<VehicleDto>.Fail("NOT_FOUND", "Vehicle not found"));

        if (dto.Name != null) vehicle.Name = dto.Name;
        if (dto.VehicleType != null) vehicle.VehicleType = dto.VehicleType;
        if (dto.Make != null) vehicle.Make = dto.Make;
        if (dto.Model != null) vehicle.Model = dto.Model;
        if (dto.Status != null) vehicle.Status = (VehicleStatus)dto.Status.Value;
        if (dto.DriverId != null) vehicle.DriverId = dto.DriverId;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<VehicleDto>.Ok(new VehicleDto
        {
            Id = vehicle.Id, RegistrationNumber = vehicle.RegistrationNumber, Name = vehicle.Name,
            Make = vehicle.Make, Model = vehicle.Model, Year = vehicle.Year,
            FuelType = (int)vehicle.FuelType, CompanyId = vehicle.CompanyId, Status = (int)vehicle.Status
        }));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id)
    {
        var vehicle = await _db.Vehicles.FindAsync(id);
        if (vehicle == null || vehicle.IsDeleted) return NotFound(ApiResponse.Fail("NOT_FOUND", "Vehicle not found"));

        // Enforce tenant isolation
        var tenantId = User.FindFirst("tenant_id")?.Value;
        if (Guid.TryParse(tenantId, out var tid) && vehicle.CompanyId != tid)
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Vehicle not found"));

        vehicle.IsDeleted = true; vehicle.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "Vehicle deleted"));
    }
}
