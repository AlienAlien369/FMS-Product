using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Application.Interfaces;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
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
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<PagedResult<DriverDto>> GetListAsync(PagedRequest filter)
    {
        var query = _db.Drivers.AsNoTracking().Where(d => !d.IsDeleted).AsQueryable();
        if (_tenant.TenantId.HasValue) query = query.Where(d => d.CompanyId == _tenant.TenantId.Value);

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

        return new PagedResult<DriverDto>
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<DriverDto> CreateAsync(CreateDriverDto dto, string userId)
    {
        var tenantId = _tenant.TenantId ?? throw new UnauthorizedAccessException("No tenant context");
        var driver = new Driver
        {
            Id = Guid.NewGuid(),
            EmployeeId = dto.EmployeeId,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            PhoneNumber = dto.PhoneNumber,
            Email = dto.Email,
            LicenseNumber = dto.LicenseNumber,
            LicenseExpiry = dto.LicenseExpiry,
            Address = dto.Address,
            City = dto.City,
            Country = dto.Country,
            CompanyId = tenantId,
            Status = DriverStatus.Active
        };
        _db.Drivers.Add(driver);
        await _db.SaveChangesAsync();
        return MapToDto(driver);
    }

    public async Task<DriverDto?> UpdateAsync(Guid id, UpdateDriverDto dto, string userId)
    {
        var driver = await _db.Drivers.FindAsync(id);
        if (driver == null || driver.IsDeleted) return null;
        if (dto.FirstName != null) driver.FirstName = dto.FirstName;
        if (dto.LastName != null) driver.LastName = dto.LastName;
        if (dto.PhoneNumber != null) driver.PhoneNumber = dto.PhoneNumber;
        if (dto.Email != null) driver.Email = dto.Email;
        if (dto.LicenseNumber != null) driver.LicenseNumber = dto.LicenseNumber;
        if (dto.LicenseExpiry != null) driver.LicenseExpiry = dto.LicenseExpiry;
        if (dto.Status != null) driver.Status = (DriverStatus)dto.Status.Value;
        await _db.SaveChangesAsync();
        return MapToDto(driver);
    }

    public async Task<bool> SoftDeleteAsync(Guid id, string userId, string? reason = null)
    {
        var driver = await _db.Drivers.FindAsync(id);
        if (driver == null || driver.IsDeleted) return false;
        driver.IsDeleted = true; driver.DeletedAt = DateTime.UtcNow; driver.DeletedBy = userId;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RestoreAsync(Guid id, string userId)
    {
        var driver = await _db.Drivers.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == id && d.IsDeleted);
        if (driver == null) return false;
        driver.IsDeleted = false; driver.DeletedAt = null; driver.DeletedBy = null;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<AuditEntryDto>> GetAuditHistoryAsync(Guid entityId)
    {
        return await _db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == EntityType.Driver && a.EntityId == entityId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new AuditEntryDto { Id = a.Id, Action = (int)a.Action, EntityType = (int)a.EntityType, EntityId = a.EntityId, CreatedAt = a.CreatedAt })
            .ToListAsync();
    }

    private static DriverDto MapToDto(Driver d) => new()
    {
        Id = d.Id, EmployeeId = d.EmployeeId, FirstName = d.FirstName, LastName = d.LastName,
        FullName = d.FullName, PhoneNumber = d.PhoneNumber, Email = d.Email,
        LicenseNumber = d.LicenseNumber, LicenseExpiry = d.LicenseExpiry,
        CompanyId = d.CompanyId, Status = (int)d.Status,
        SafetyScore = d.SafetyScore, BehaviourScore = d.BehaviourScore
    };
}
