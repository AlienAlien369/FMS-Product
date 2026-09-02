using Freebuff.Platform.Application.DTOs;
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
