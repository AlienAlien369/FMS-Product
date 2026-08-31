using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Application.Interfaces;
using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

public class CompanyService : ICrudService<CompanyDto, CreateCompanyDto, UpdateCompanyDto, PagedRequest>
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public CompanyService(ApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<CompanyDto?> GetByIdAsync(Guid id)
    {
        var query = _db.Companies
            .AsNoTracking()
            .Where(c => c.Id == id && !c.IsDeleted);

        // Non-SuperAdmin can only see their own company
        if (!_tenant.IsSuperAdmin && _tenant.TenantId.HasValue)
            query = query.Where(c => c.Id == _tenant.TenantId.Value);

        var entity = await query.FirstOrDefaultAsync();
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<PagedResult<CompanyDto>> GetListAsync(PagedRequest filter)
    {
        var query = _db.Companies
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .AsQueryable();

        // Non-SuperAdmin can only see their own company
        if (!_tenant.IsSuperAdmin && _tenant.TenantId.HasValue)
            query = query.Where(c => c.Id == _tenant.TenantId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(c => c.Name.Contains(filter.Search) || (c.Slug != null && c.Slug.Contains(filter.Search)));

        var totalCount = await query.CountAsync();

        query = filter.SortBy?.ToLower() switch
        {
            "name" => filter.SortDescending ? query.OrderByDescending(c => c.Name) : query.OrderBy(c => c.Name),
            "createdat" => filter.SortDescending ? query.OrderByDescending(c => c.CreatedAt) : query.OrderBy(c => c.CreatedAt),
            _ => query.OrderBy(c => c.Name)
        };

        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(c => new CompanyDto
            {
                Id = c.Id,
                Name = c.Name,
                Slug = c.Slug,
                LogoUrl = c.LogoUrl,
                ContactEmail = c.ContactEmail,
                ContactPhone = c.ContactPhone,
                Country = c.Country,
                DefaultLanguage = c.DefaultLanguage,
                DefaultTimezone = c.DefaultTimezone,
                DefaultCurrency = c.DefaultCurrency,
                Status = (int)c.Status,
                CreatedAt = c.CreatedAt,
                UserCount = c.Users.Count(u => !u.IsDeleted),
                VehicleCount = c.Vehicles.Count(v => !v.IsDeleted)
            })
            .ToListAsync();

        return new PagedResult<CompanyDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<CompanyDto> CreateAsync(CreateCompanyDto dto, string userId)
    {
        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Slug = dto.Slug ?? dto.Name.ToLowerInvariant().Replace(" ", "-"),
            ContactEmail = dto.ContactEmail,
            ContactPhone = dto.ContactPhone,
            Address = dto.Address,
            City = dto.City,
            Country = dto.Country,
            DefaultLanguage = dto.DefaultLanguage,
            DefaultTimezone = dto.DefaultTimezone,
            DefaultCurrency = dto.DefaultCurrency,
            PackageId = dto.PackageId,
            Status = EntityStatus.Active
        };

        _db.Companies.Add(company);
        await _db.SaveChangesAsync();

        return MapToDto(company);
    }

    public async Task<CompanyDto?> UpdateAsync(Guid id, UpdateCompanyDto dto, string userId)
    {
        var company = await _db.Companies.FindAsync(id);
        if (company == null || company.IsDeleted) return null;

        // Non-SuperAdmin can only update their own company
        if (!_tenant.IsSuperAdmin && company.Id != _tenant.TenantId)
            return null;

        if (dto.Name != null) company.Name = dto.Name;
        if (dto.ContactEmail != null) company.ContactEmail = dto.ContactEmail;
        if (dto.ContactPhone != null) company.ContactPhone = dto.ContactPhone;
        if (dto.Address != null) company.Address = dto.Address;
        if (dto.City != null) company.City = dto.City;
        if (dto.Country != null) company.Country = dto.Country;
        if (dto.DefaultLanguage != null) company.DefaultLanguage = dto.DefaultLanguage;
        if (dto.DefaultTimezone != null) company.DefaultTimezone = dto.DefaultTimezone;
        if (dto.DefaultCurrency != null) company.DefaultCurrency = dto.DefaultCurrency;
        if (dto.Status != null) company.Status = (EntityStatus)dto.Status.Value;

        await _db.SaveChangesAsync();
        return MapToDto(company);
    }

    public async Task<bool> SoftDeleteAsync(Guid id, string userId, string? reason = null)
    {
        var company = await _db.Companies.FindAsync(id);
        if (company == null || company.IsDeleted) return false;

        // Only SuperAdmin can delete companies
        if (!_tenant.IsSuperAdmin) return false;

        company.IsDeleted = true;
        company.DeletedAt = DateTime.UtcNow;
        company.DeletedBy = userId;
        company.DeletionReason = reason;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RestoreAsync(Guid id, string userId)
    {
        var company = await _db.Companies.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == id && c.IsDeleted);
        if (company == null) return false;

        // Only SuperAdmin can restore companies
        if (!_tenant.IsSuperAdmin) return false;

        company.IsDeleted = false;
        company.DeletedAt = null;
        company.DeletedBy = null;
        company.DeletionReason = null;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<AuditEntryDto>> GetAuditHistoryAsync(Guid entityId)
    {
        return await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityType == EntityType.Company && a.EntityId == entityId)
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

    private static CompanyDto MapToDto(Company c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Slug = c.Slug,
        LogoUrl = c.LogoUrl,
        ContactEmail = c.ContactEmail,
        ContactPhone = c.ContactPhone,
        Country = c.Country,
        DefaultLanguage = c.DefaultLanguage,
        DefaultTimezone = c.DefaultTimezone,
        DefaultCurrency = c.DefaultCurrency,
        Status = (int)c.Status,
        CreatedAt = c.CreatedAt
    };
}
