using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Application.Interfaces;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

public class VehicleService : ICrudService<VehicleDto, CreateVehicleDto, UpdateVehicleDto, PagedRequest>
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public VehicleService(ApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<VehicleDto?> GetByIdAsync(Guid id)
    {
        var entity = await _db.Vehicles
            .AsNoTracking()
            .Include(v => v.Driver)
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);

        return entity == null ? null : MapToDto(entity);
    }

    public async Task<PagedResult<VehicleDto>> GetListAsync(PagedRequest filter)
    {
        var query = _db.Vehicles
            .AsNoTracking()
            .Include(v => v.Driver)
            .Where(v => !v.IsDeleted)
            .AsQueryable();

        // Enforce tenant isolation
        if (_tenant.TenantId.HasValue)
            query = query.Where(v => v.CompanyId == _tenant.TenantId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(v =>
                v.RegistrationNumber.Contains(filter.Search) ||
                (v.Name != null && v.Name.Contains(filter.Search)) ||
                (v.Make != null && v.Make.Contains(filter.Search)) ||
                (v.Model != null && v.Model.Contains(filter.Search)));

        var totalCount = await query.CountAsync();

        query = filter.SortBy?.ToLower() switch
        {
            "registrationnumber" => filter.SortDescending
                ? query.OrderByDescending(v => v.RegistrationNumber)
                : query.OrderBy(v => v.RegistrationNumber),
            "make" => filter.SortDescending ? query.OrderByDescending(v => v.Make) : query.OrderBy(v => v.Make),
            "status" => filter.SortDescending ? query.OrderByDescending(v => v.Status) : query.OrderBy(v => v.Status),
            _ => query.OrderBy(v => v.RegistrationNumber)
        };

        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(v => new VehicleDto
            {
                Id = v.Id,
                RegistrationNumber = v.RegistrationNumber,
                Name = v.Name,
                VehicleType = v.VehicleType,
                Make = v.Make,
                Model = v.Model,
                Year = v.Year,
                FuelType = (int)v.FuelType,
                CompanyId = v.CompanyId,
                DriverId = v.DriverId,
                DriverName = v.Driver != null ? $"{v.Driver.FirstName} {v.Driver.LastName}" : null,
                Status = (int)v.Status,
                LastLatitude = v.LastLatitude,
                LastLongitude = v.LastLongitude,
                LastSpeed = v.LastSpeed,
                LastLocationUpdate = v.LastLocationUpdate,
                IgnitionStatus = v.IgnitionStatus
            })
            .ToListAsync();

        return new PagedResult<VehicleDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<VehicleDto> CreateAsync(CreateVehicleDto dto, string userId)
    {
        var tenantId = _tenant.TenantId ?? throw new UnauthorizedAccessException("No tenant context");

        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            RegistrationNumber = dto.RegistrationNumber,
            Name = dto.Name,
            VehicleType = dto.VehicleType,
            Make = dto.Make,
            Model = dto.Model,
            Year = dto.Year,
            FuelType = (FuelType)dto.FuelType,
            FuelTankCapacity = dto.FuelTankCapacity,
            EngineNumber = dto.EngineNumber,
            ChassisNumber = dto.ChassisNumber,
            DeviceImei = dto.DeviceImei,
            CompanyId = tenantId,
            Status = VehicleStatus.Active
        };

        _db.Vehicles.Add(vehicle);
        await _db.SaveChangesAsync();

        return MapToDto(vehicle);
    }

    public async Task<VehicleDto?> UpdateAsync(Guid id, UpdateVehicleDto dto, string userId)
    {
        var vehicle = await _db.Vehicles.FindAsync(id);
        if (vehicle == null || vehicle.IsDeleted) return null;

        if (dto.Name != null) vehicle.Name = dto.Name;
        if (dto.VehicleType != null) vehicle.VehicleType = dto.VehicleType;
        if (dto.Make != null) vehicle.Make = dto.Make;
        if (dto.Model != null) vehicle.Model = dto.Model;
        if (dto.Year != null) vehicle.Year = dto.Year;
        if (dto.FuelType != null) vehicle.FuelType = (FuelType)dto.FuelType.Value;
        if (dto.FuelTankCapacity != null) vehicle.FuelTankCapacity = dto.FuelTankCapacity;
        if (dto.EngineNumber != null) vehicle.EngineNumber = dto.EngineNumber;
        if (dto.ChassisNumber != null) vehicle.ChassisNumber = dto.ChassisNumber;
        if (dto.DeviceImei != null) vehicle.DeviceImei = dto.DeviceImei;
        if (dto.DriverId != null) vehicle.DriverId = dto.DriverId;
        if (dto.Status != null) vehicle.Status = (VehicleStatus)dto.Status.Value;

        await _db.SaveChangesAsync();
        return MapToDto(vehicle);
    }

    public async Task<bool> SoftDeleteAsync(Guid id, string userId, string? reason = null)
    {
        var vehicle = await _db.Vehicles.FindAsync(id);
        if (vehicle == null || vehicle.IsDeleted) return false;

        vehicle.IsDeleted = true;
        vehicle.DeletedAt = DateTime.UtcNow;
        vehicle.DeletedBy = userId;
        vehicle.DeletionReason = reason;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RestoreAsync(Guid id, string userId)
    {
        var vehicle = await _db.Vehicles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.Id == id && v.IsDeleted);
        if (vehicle == null) return false;

        vehicle.IsDeleted = false;
        vehicle.DeletedAt = null;
        vehicle.DeletedBy = null;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<AuditEntryDto>> GetAuditHistoryAsync(Guid entityId)
    {
        return await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityType == EntityType.Vehicle && a.EntityId == entityId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new AuditEntryDto
            {
                Id = a.Id,
                Action = (int)a.Action,
                EntityType = (int)a.EntityType,
                EntityId = a.EntityId,
                EntityName = a.EntityName,
                OldValues = a.OldValues,
                NewValues = a.NewValues,
                UserId = a.UserId.ToString(),
                UserName = a.UserName,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();
    }

    private static VehicleDto MapToDto(Vehicle v) => new()
    {
        Id = v.Id,
        RegistrationNumber = v.RegistrationNumber,
        Name = v.Name,
        VehicleType = v.VehicleType,
        Make = v.Make,
        Model = v.Model,
        Year = v.Year,
        FuelType = (int)v.FuelType,
        CompanyId = v.CompanyId,
        DriverId = v.DriverId,
        DriverName = v.Driver != null ? $"{v.Driver.FirstName} {v.Driver.LastName}" : null,
        Status = (int)v.Status,
        LastLatitude = v.LastLatitude,
        LastLongitude = v.LastLongitude,
        LastSpeed = v.LastSpeed,
        LastLocationUpdate = v.LastLocationUpdate,
        IgnitionStatus = v.IgnitionStatus
    };
}
