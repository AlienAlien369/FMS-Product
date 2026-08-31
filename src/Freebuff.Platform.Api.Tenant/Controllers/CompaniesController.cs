using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Api.Tenant.Data;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Tenant.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class CompaniesController : ControllerBase
{
    private readonly TenantDbContext _db;

    public CompaniesController(TenantDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<CompanyDto>>>> GetAll([FromQuery] PagedRequest filter)
    {
        var query = _db.Companies.AsNoTracking().Where(c => !c.IsDeleted).AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(c => c.Name.Contains(filter.Search));

        var totalCount = await query.CountAsync();
        var items = await query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(c => new CompanyDto
            {
                Id = c.Id, Name = c.Name, Slug = c.Slug, ContactEmail = c.ContactEmail,
                ContactPhone = c.ContactPhone, Country = c.Country, DefaultLanguage = c.DefaultLanguage,
                DefaultTimezone = c.DefaultTimezone, DefaultCurrency = c.DefaultCurrency,
                Status = (int)c.Status, CreatedAt = c.CreatedAt
            }).ToListAsync();

        return Ok(ApiResponse<PagedResult<CompanyDto>>.Ok(new PagedResult<CompanyDto>
        {
            Items = items, TotalCount = totalCount, Page = filter.Page, PageSize = filter.PageSize
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CompanyDto>>> GetById(Guid id)
    {
        var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
        if (company == null) return NotFound(ApiResponse<CompanyDto>.Fail("NOT_FOUND", "Company not found"));
        return Ok(ApiResponse<CompanyDto>.Ok(new CompanyDto
        {
            Id = company.Id, Name = company.Name, Slug = company.Slug, ContactEmail = company.ContactEmail,
            ContactPhone = company.ContactPhone, Country = company.Country, DefaultLanguage = company.DefaultLanguage,
            DefaultTimezone = company.DefaultTimezone, DefaultCurrency = company.DefaultCurrency,
            Status = (int)company.Status, CreatedAt = company.CreatedAt
        }));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<CompanyDto>>> Create([FromBody] CreateCompanyDto dto)
    {
        var company = new Company
        {
            Id = Guid.NewGuid(), Name = dto.Name,
            Slug = dto.Slug ?? dto.Name.ToLowerInvariant().Replace(" ", "-"),
            ContactEmail = dto.ContactEmail, ContactPhone = dto.ContactPhone,
            Country = dto.Country, DefaultLanguage = dto.DefaultLanguage,
            DefaultTimezone = dto.DefaultTimezone, DefaultCurrency = dto.DefaultCurrency,
            Status = EntityStatus.Active
        };
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = company.Id }, ApiResponse<CompanyDto>.Ok(new CompanyDto
        {
            Id = company.Id, Name = company.Name, Status = (int)company.Status
        }));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CompanyDto>>> Update(Guid id, [FromBody] UpdateCompanyDto dto)
    {
        var company = await _db.Companies.FindAsync(id);
        if (company == null || company.IsDeleted) return NotFound(ApiResponse<CompanyDto>.Fail("NOT_FOUND", "Company not found"));
        if (dto.Name != null) company.Name = dto.Name;
        if (dto.ContactEmail != null) company.ContactEmail = dto.ContactEmail;
        if (dto.Country != null) company.Country = dto.Country;
        if (dto.Status != null) company.Status = (EntityStatus)dto.Status.Value;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<CompanyDto>.Ok(new CompanyDto { Id = company.Id, Name = company.Name, Status = (int)company.Status }));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id)
    {
        var company = await _db.Companies.FindAsync(id);
        if (company == null || company.IsDeleted) return NotFound(ApiResponse.Fail("NOT_FOUND", "Company not found"));
        company.IsDeleted = true; company.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "Company deleted"));
    }
}
