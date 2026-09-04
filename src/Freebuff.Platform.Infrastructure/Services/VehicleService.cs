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
        var query = _db.Vehicles
            .AsNoTracking()
            .Include(v => v.Driver)
            .Include(v => v.Client)
            .Where(v => v.Id == id && !v.IsDeleted);

        if (!_tenant.IsSuperAdmin && _tenant.TenantId.HasValue)
            query = query.Where(v => v.CompanyId == _tenant.TenantId.Value);

        var entity = await query.FirstOrDefaultAsync();
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<PagedResult<VehicleDto>> GetListAsync(PagedRequest filter)
    {
        var query = _db.Vehicles
            .AsNoTracking()
            .Include(v => v.Driver)
            .Include(v => v.Client)
            .Where(v => !v.IsDeleted)
            .AsQueryable();

        if (!_tenant.IsSuperAdmin && _tenant.TenantId.HasValue)
            query = query.Where(v => v.CompanyId == _tenant.TenantId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(v =>
                v.RegistrationNumber.Contains(filter.Search) ||
                (v.Name != null && v.Name.Contains(filter.Search)) ||
                (v.Make != null && v.Make.Contains(filter.Search)) ||
                (v.Model != null && v.Model.Contains(filter.Search)) ||
                (v.VehicleType != null && v.VehicleType.Contains(filter.Search)));

        var totalCount = await query.CountAsync();

        query = filter.SortBy?.ToLower() switch
        {
            "registrationnumber" => filter.SortDescending
                ? query.OrderByDescending(v => v.RegistrationNumber)
                : query.OrderBy(v => v.RegistrationNumber),
            "make" => filter.SortDescending ? query.OrderByDescending(v => v.Make) : query.OrderBy(v => v.Make),
            "status" => filter.SortDescending ? query.OrderByDescending(v => v.Status) : query.OrderBy(v => v.Status),
            "name" => filter.SortDescending ? query.OrderByDescending(v => v.Name) : query.OrderBy(v => v.Name),
            "year" => filter.SortDescending ? query.OrderByDescending(v => v.Year) : query.OrderBy(v => v.Year),
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
                Color = v.Color,
                FuelType = (int)v.FuelType,
                FuelTankCapacity = v.FuelTankCapacity,
                FuelCapacityUnit = v.FuelCapacityUnit,
                EngineNumber = v.EngineNumber,
                ChassisNumber = v.ChassisNumber,
                VinNumber = v.VinNumber,
                CompanyId = v.CompanyId,
                DriverId = v.DriverId,
                DriverName = v.Driver != null ? v.Driver.FirstName + " " + v.Driver.LastName : null,
                ClientId = v.ClientId,
                ClientName = v.Client != null ? v.Client.Name : null,
                Status = (int)v.Status,
                DeviceImei = v.DeviceImei,
                DeviceType = v.DeviceType,
                DeviceSerialNumber = v.DeviceSerialNumber,
                LastLatitude = v.LastLatitude,
                LastLongitude = v.LastLongitude,
                LastSpeed = v.LastSpeed,
                LastHeading = v.LastHeading,
                LastLocationUpdate = v.LastLocationUpdate,
                IgnitionStatus = v.IgnitionStatus,
                OdometerReading = v.OdometerReading,
                EngineHours = v.EngineHours,
                CreatedAt = v.CreatedAt
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
            Color = dto.Color,
            FuelType = (FuelType)dto.FuelType,
            FuelTankCapacity = dto.FuelTankCapacity,
            FuelCapacityUnit = dto.FuelCapacityUnit,
            EngineNumber = dto.EngineNumber,
            ChassisNumber = dto.ChassisNumber,
            VinNumber = dto.VinNumber,
            DriverId = dto.DriverId,
            ClientId = dto.ClientId,
            DeviceImei = dto.DeviceImei,
            DeviceType = dto.DeviceType,
            DeviceSerialNumber = dto.DeviceSerialNumber,
            CompanyId = tenantId,
            Status = VehicleStatus.Active
        };

        _db.Vehicles.Add(vehicle);
        await _db.SaveChangesAsync();

        return MapToDto(vehicle);
    }

    public async Task<VehicleDto?> UpdateAsync(Guid id, UpdateVehicleDto dto, string userId)
    {
        var query = _db.Vehicles.Include(v => v.Client).Where(v => v.Id == id && !v.IsDeleted);
        if (!_tenant.IsSuperAdmin && _tenant.TenantId.HasValue)
            query = query.Where(v => v.CompanyId == _tenant.TenantId.Value);
        var vehicle = await query.FirstOrDefaultAsync();
        if (vehicle == null) return null;

        if (dto.Name != null) vehicle.Name = dto.Name;
        if (dto.VehicleType != null) vehicle.VehicleType = dto.VehicleType;
        if (dto.Make != null) vehicle.Make = dto.Make;
        if (dto.Model != null) vehicle.Model = dto.Model;
        if (dto.Year != null) vehicle.Year = dto.Year;
        if (dto.Color != null) vehicle.Color = dto.Color;
        if (dto.FuelType != null) vehicle.FuelType = (FuelType)dto.FuelType.Value;
        if (dto.FuelTankCapacity != null) vehicle.FuelTankCapacity = dto.FuelTankCapacity;
        if (dto.FuelCapacityUnit != null) vehicle.FuelCapacityUnit = dto.FuelCapacityUnit;
        if (dto.EngineNumber != null) vehicle.EngineNumber = dto.EngineNumber;
        if (dto.ChassisNumber != null) vehicle.ChassisNumber = dto.ChassisNumber;
        if (dto.VinNumber != null) vehicle.VinNumber = dto.VinNumber;
        if (dto.DriverId != null) vehicle.DriverId = dto.DriverId;
        if (dto.ClientId != null) vehicle.ClientId = dto.ClientId;
        if (dto.DeviceImei != null) vehicle.DeviceImei = dto.DeviceImei;
        if (dto.DeviceType != null) vehicle.DeviceType = dto.DeviceType;
        if (dto.DeviceSerialNumber != null) vehicle.DeviceSerialNumber = dto.DeviceSerialNumber;
        if (dto.Status != null) vehicle.Status = (VehicleStatus)dto.Status.Value;
        if (dto.OdometerReading != null) vehicle.OdometerReading = dto.OdometerReading;
        if (dto.EngineHours != null) vehicle.EngineHours = dto.EngineHours;

        await _db.SaveChangesAsync();
        return MapToDto(vehicle);
    }

    public async Task<bool> SoftDeleteAsync(Guid id, string userId, string? reason = null)
    {
        var query = _db.Vehicles.Where(v => v.Id == id && !v.IsDeleted);
        if (!_tenant.IsSuperAdmin && _tenant.TenantId.HasValue)
            query = query.Where(v => v.CompanyId == _tenant.TenantId.Value);
        var vehicle = await query.FirstOrDefaultAsync();
        if (vehicle == null) return false;

        vehicle.IsDeleted = true;
        vehicle.DeletedAt = DateTime.UtcNow;
        vehicle.DeletedBy = userId;
        vehicle.DeletionReason = reason;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RestoreAsync(Guid id, string userId)
    {
        var query = _db.Vehicles.IgnoreQueryFilters().Where(v => v.Id == id && v.IsDeleted);
        if (!_tenant.IsSuperAdmin && _tenant.TenantId.HasValue)
            query = query.Where(v => v.CompanyId == _tenant.TenantId.Value);
        var vehicle = await query.FirstOrDefaultAsync();
        if (vehicle == null) return false;

        vehicle.IsDeleted = false;
        vehicle.DeletedAt = null;
        vehicle.DeletedBy = null;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<AuditEntryDto>> GetAuditHistoryAsync(Guid entityId)
    {
        var query = _db.AuditLogs.AsNoTracking().Where(a => a.EntityType == EntityType.Vehicle && a.EntityId == entityId);
        if (!_tenant.IsSuperAdmin && _tenant.TenantId.HasValue)
            query = query.Where(a => a.TenantId == _tenant.TenantId.Value);
        return await query
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
        Color = v.Color,
        FuelType = (int)v.FuelType,
        FuelTankCapacity = v.FuelTankCapacity,
        FuelCapacityUnit = v.FuelCapacityUnit,
        EngineNumber = v.EngineNumber,
        ChassisNumber = v.ChassisNumber,
        VinNumber = v.VinNumber,
        CompanyId = v.CompanyId,
        DriverId = v.DriverId,
        DriverName = v.Driver != null ? $"{v.Driver.FirstName} {v.Driver.LastName}" : null,
        ClientId = v.ClientId,
        ClientName = v.Client?.Name,
        Status = (int)v.Status,
        DeviceImei = v.DeviceImei,
        DeviceType = v.DeviceType,
        DeviceSerialNumber = v.DeviceSerialNumber,
        LastLatitude = v.LastLatitude,
        LastLongitude = v.LastLongitude,
        LastSpeed = v.LastSpeed,
        LastHeading = v.LastHeading,
        LastLocationUpdate = v.LastLocationUpdate,
        IgnitionStatus = v.IgnitionStatus,
        OdometerReading = v.OdometerReading,
        EngineHours = v.EngineHours,
        CreatedAt = v.CreatedAt
    };
}
