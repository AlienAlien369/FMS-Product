using Freebuff.Platform.Api.Authorization;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Shared.Extensions;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Controllers;

/// <summary>
/// Provides tenant-scoped lookup data (drivers, clients, etc.) for any authenticated user.
/// Uses the user's tenant_id claim for scoping instead of requiring SuperAdmin role.
/// </summary>
[ApiController]
[Route("api/v1/tenant")]
[Authorize]
public class TenantController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public TenantController(ApplicationDbContext db) => _db = db;

    private Guid GetTenantId() => User.GetTenantId();

    [HttpGet("drivers")]
    public async Task<ActionResult<ApiResponse<object>>> GetDrivers()
    {
        var cid = GetTenantId();
        var drivers = await _db.Drivers.AsNoTracking()
            .Where(d => d.CompanyId == cid && !d.IsDeleted)
            .OrderBy(d => d.FirstName)
            .Select(d => new
            {
                d.Id, d.EmployeeId, d.FirstName, d.LastName,
                FullName = d.FirstName + " " + d.LastName,
                Status = (int)d.Status
            }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(drivers));
    }

    [HttpGet("clients")]
    public async Task<ActionResult<ApiResponse<object>>> GetClients()
    {
        var cid = GetTenantId();
        var clients = await _db.Clients.AsNoTracking()
            .Where(c => c.CompanyId == cid && !c.IsDeleted)
            .OrderBy(c => c.Name)
            .Select(c => new
            {
                c.Id, c.Name, c.ContactPerson, c.ContactEmail,
                Status = (int)c.Status
            }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(clients));
    }

    [HttpGet("company")]
    public async Task<ActionResult<ApiResponse<object>>> GetCompany()
    {
        var cid = GetTenantId();
        var company = await _db.Companies.AsNoTracking()
            .Where(c => c.Id == cid && !c.IsDeleted)
            .Select(c => new
            {
                c.Id, c.Name, c.Slug, c.ContactEmail, c.ContactPhone,
                c.Website, c.Address, c.City, c.State, c.Country, c.PostalCode,
                c.DefaultLanguage, c.DefaultTimezone, c.DefaultCurrency,
                c.DateFormat, c.TimeFormat, c.LogoUrl, c.FaviconUrl,
                DefaultMapProvider = c.DefaultMapProvider.ToString(), c.MapApiKey, Status = (int)c.Status
            }).FirstOrDefaultAsync();
        return Ok(ApiResponse<object>.Ok(company));
    }

    /// <summary>
    /// Company-scoped settings: a company admin (or super admin acting for a
    /// company) may override their own company's default language/currency/etc.
    /// The company can only pick values that exist and are Active in the
    /// platform-wide master lists (Languages / Currencies).
    /// </summary>
    [HttpPut("company-settings")]
    [RequirePermission("settings.update")]
    public async Task<ActionResult<ApiResponse>> UpdateCompanySettings([FromBody] UpdateCompanyDto dto)
    {
        var cid = GetTenantId();
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == cid && !c.IsDeleted);
        if (company == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Company not found"));

        if (!string.IsNullOrWhiteSpace(dto.DefaultLanguage) &&
            !await _db.Languages.AnyAsync(l => l.Code == dto.DefaultLanguage && !l.IsDeleted && l.Status == EntityStatus.Active))
            return BadRequest(ApiResponse.Fail("INVALID_LOCALE", $"Language '{dto.DefaultLanguage}' is not an active language."));
        if (!string.IsNullOrWhiteSpace(dto.DefaultCurrency) &&
            !await _db.Currencies.AnyAsync(c => c.Code == dto.DefaultCurrency && !c.IsDeleted && c.Status == EntityStatus.Active))
            return BadRequest(ApiResponse.Fail("INVALID_LOCALE", $"Currency '{dto.DefaultCurrency}' is not an active currency."));

        if (dto.Name != null && dto.Name != company.Name)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(ApiResponse.Fail("VALIDATION", "Company name cannot be empty"));
            company.Name = dto.Name;
        }
        if (dto.LogoUrl != null) company.LogoUrl = dto.LogoUrl;
        if (dto.ContactEmail != null) company.ContactEmail = dto.ContactEmail;
        if (dto.ContactPhone != null) company.ContactPhone = dto.ContactPhone;
        if (dto.Website != null) company.Website = dto.Website;
        if (dto.Address != null) company.Address = dto.Address;
        if (dto.City != null) company.City = dto.City;
        if (dto.State != null) company.State = dto.State;
        if (dto.Country != null) company.Country = dto.Country;
        if (dto.PostalCode != null) company.PostalCode = dto.PostalCode;
        if (dto.DefaultLanguage != null) company.DefaultLanguage = dto.DefaultLanguage;
        if (dto.DefaultTimezone != null) company.DefaultTimezone = dto.DefaultTimezone;
        if (dto.DefaultCurrency != null) company.DefaultCurrency = dto.DefaultCurrency;
        if (dto.DateFormat != null) company.DateFormat = dto.DateFormat;
        if (dto.TimeFormat != null) company.TimeFormat = dto.TimeFormat;
        if (dto.NumberFormat != null) company.NumberFormat = dto.NumberFormat;
        if (dto.DefaultMapProvider.HasValue) company.DefaultMapProvider = (MapProvider)dto.DefaultMapProvider.Value;

        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "Company settings updated"));
    }

    [HttpGet("subscription")]
    public async Task<ActionResult<ApiResponse<object>>> GetSubscription()
    {
        var cid = GetTenantId();
        var sub = await _db.Subscriptions.AsNoTracking()
            .Include(s => s.Package)
            .Where(s => s.CompanyId == cid && !s.IsDeleted)
            .OrderByDescending(s => s.StartDate)
            .Select(s => new
            {
                s.Id, s.CompanyId, s.PackageId,
                PackageName = s.Package.Name,
                Status = (int)s.Status, s.StartDate, s.EndDate,
                s.CurrentPrice, s.Currency, s.BillingCycle,
                EffectivePrice = s.CurrentPrice * (1 - (s.DiscountPercentage ?? 0) / 100) * (1 + (s.TaxPercentage ?? 0) / 100),
                MaxUsers = s.MaxUsers ?? s.Package.MaxUsers,
                MaxVehicles = s.MaxVehicles ?? s.Package.MaxVehicles,
                MaxDrivers = s.MaxDrivers ?? s.Package.MaxDrivers
            }).FirstOrDefaultAsync();
        return Ok(ApiResponse<object>.Ok(sub));
    }
}
