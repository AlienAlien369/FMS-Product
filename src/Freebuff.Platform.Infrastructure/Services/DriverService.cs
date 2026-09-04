using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Application.Interfaces;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.CompanyScope;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

public class DriverService : ICrudService<DriverDto, CreateDriverDto, UpdateDriverDto, PagedRequest>
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public DriverService(ApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<DriverDto?> GetByIdAsync(Guid id)
    {
        var entity = await _db.Drivers
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);
        return entity == null ? null : await MapToDtoAsync(entity);
    }

    public Task<PagedResult<DriverDto>> GetListAsync(PagedRequest filter)
        => GetListAsync(filter, null);

    /// <summary>List with an optional status filter (the Drivers UI status buttons).</summary>
    public async Task<PagedResult<DriverDto>> GetListAsync(PagedRequest filter, int? status)
    {
        var query = _db.Drivers.AsNoTracking().Where(d => !d.IsDeleted).AsQueryable();
        // Query-side: effective scope = X-Company-Scope ∩ permitted set (list view).
        query = query.InEffectiveCompanyScope(_tenant.Scope, d => d.CompanyId);

        if (status.HasValue)
            query = query.Where(d => (int)d.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(d => d.FirstName.Contains(filter.Search) || d.LastName.Contains(filter.Search) || d.EmployeeId.Contains(filter.Search));

        var totalCount = await query.CountAsync();
        query = filter.SortBy?.ToLower() switch
        {
            "firstname" => filter.SortDescending ? query.OrderByDescending(d => d.FirstName) : query.OrderBy(d => d.FirstName),
            "lastname" => filter.SortDescending ? query.OrderByDescending(d => d.LastName) : query.OrderBy(d => d.LastName),
            _ => query.OrderBy(d => d.LastName)
        };

        var items = await query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToListAsync();
        var dtos = new List<DriverDto>();
        foreach (var item in items) dtos.Add(await MapToDtoAsync(item));

        return new PagedResult<DriverDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }    public async Task<DriverDto> CreateAsync(CreateDriverDto dto, string userId)
    {
        var tenantId = _tenant.TenantId ?? throw new UnauthorizedAccessException("No tenant context");
        var driver = new Driver
        {
            Id = Guid.NewGuid(), EmployeeId = dto.EmployeeId,
            FirstName = dto.FirstName, LastName = dto.LastName,
            PhoneNumber = dto.PhoneNumber, Email = dto.Email,
            LicenseNumber = dto.LicenseNumber, LicenseExpiry = dto.LicenseExpiry,
            LicenseCategory = dto.LicenseCategory,
            Address = dto.Address, City = dto.City, Country = dto.Country,
            ProfileImageUrl = dto.ProfileImageUrl,
            CompanyId = tenantId, Status = DriverStatus.Active
        };
        _db.Drivers.Add(driver);
        await _db.SaveChangesAsync();
        return await MapToDtoAsync(driver);
    }

    public async Task<DriverDto?> UpdateAsync(Guid id, UpdateDriverDto dto, string userId)
    {
        var query = _db.Drivers.Where(d => d.Id == id && !d.IsDeleted);
        if (_tenant.TenantId.HasValue && !_tenant.IsSuperAdmin)
            query = query.Where(d => d.CompanyId == _tenant.TenantId.Value);
        var driver = await query.FirstOrDefaultAsync();
        if (driver == null) return null;
        if (dto.FirstName != null) driver.FirstName = dto.FirstName;
        if (dto.LastName != null) driver.LastName = dto.LastName;
        if (dto.PhoneNumber != null) driver.PhoneNumber = dto.PhoneNumber;
        if (dto.Email != null) driver.Email = dto.Email;
        if (dto.LicenseNumber != null) driver.LicenseNumber = dto.LicenseNumber;
        if (dto.LicenseExpiry != null) driver.LicenseExpiry = dto.LicenseExpiry;
        if (dto.LicenseCategory != null) driver.LicenseCategory = dto.LicenseCategory;
        if (dto.Address != null) driver.Address = dto.Address;
        if (dto.City != null) driver.City = dto.City;
        if (dto.Country != null) driver.Country = dto.Country;
        if (dto.ProfileImageUrl != null) driver.ProfileImageUrl = dto.ProfileImageUrl;
        if (dto.Status != null) driver.Status = (DriverStatus)dto.Status.Value;
        await _db.SaveChangesAsync();
        return await MapToDtoAsync(driver);
    }

    public async Task<bool> SoftDeleteAsync(Guid id, string userId, string? reason = null)
    {
        var query = _db.Drivers.Where(d => d.Id == id && !d.IsDeleted);
        if (_tenant.TenantId.HasValue && !_tenant.IsSuperAdmin)
            query = query.Where(d => d.CompanyId == _tenant.TenantId.Value);
        var driver = await query.FirstOrDefaultAsync();
        if (driver == null) return false;
        driver.IsDeleted = true; driver.DeletedAt = DateTime.UtcNow; driver.DeletedBy = userId;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RestoreAsync(Guid id, string userId)
    {
        var query = _db.Drivers.IgnoreQueryFilters().Where(d => d.Id == id && d.IsDeleted);
        if (_tenant.TenantId.HasValue && !_tenant.IsSuperAdmin)
            query = query.Where(d => d.CompanyId == _tenant.TenantId.Value);
        var driver = await query.FirstOrDefaultAsync();
        if (driver == null) return false;
        driver.IsDeleted = false; driver.DeletedAt = null; driver.DeletedBy = null;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<AuditEntryDto>> GetAuditHistoryAsync(Guid entityId)
    {
        var query = _db.AuditLogs.AsNoTracking().Where(a => a.EntityType == EntityType.Driver && a.EntityId == entityId);
        if (_tenant.TenantId.HasValue && !_tenant.IsSuperAdmin)
            query = query.Where(a => a.TenantId == _tenant.TenantId.Value);
        return await query
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new AuditEntryDto { Id = a.Id, Action = (int)a.Action, EntityType = (int)a.EntityType, EntityId = a.EntityId, CreatedAt = a.CreatedAt })
            .ToListAsync();
    }

    private async Task<DriverDto> MapToDtoAsync(Driver d)
    {
        var assignedVehicle = await _db.Vehicles.AsNoTracking()
            .Where(v => v.DriverId == d.Id && !v.IsDeleted)
            .Select(v => new { v.Id, v.RegistrationNumber })
            .FirstOrDefaultAsync();
        var tripCount = await _db.Trips.AsNoTracking()
            .CountAsync(t => t.DriverId == d.Id && !t.IsDeleted);
        var companyName = await _db.Companies.AsNoTracking()
            .Where(c => c.Id == d.CompanyId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync();
        return new DriverDto
        {
            Id = d.Id, EmployeeId = d.EmployeeId, FirstName = d.FirstName, LastName = d.LastName,
            FullName = d.FullName, PhoneNumber = d.PhoneNumber, Email = d.Email,
            LicenseNumber = d.LicenseNumber, LicenseExpiry = d.LicenseExpiry,
            LicenseCategory = d.LicenseCategory,
            Address = d.Address, City = d.City, Country = d.Country,
            ProfileImageUrl = d.ProfileImageUrl,
            CompanyId = d.CompanyId, CompanyName = companyName,
            Status = (int)d.Status, SafetyScore = d.SafetyScore, BehaviourScore = d.BehaviourScore,
            AssignedVehicleId = assignedVehicle?.Id, AssignedVehicleReg = assignedVehicle?.RegistrationNumber,
            TripCount = tripCount, CreatedAt = d.CreatedAt
        };
    }
}
