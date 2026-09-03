using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/admin/companies/{cid:guid}/subscription")]
[Authorize(Roles = "SuperAdmin")]
public class SubscriptionController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IPermissionService _permissionService;
    public SubscriptionController(ApplicationDbContext db, IPermissionService permissionService)
    {
        _db = db;
        _permissionService = permissionService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<object>>> Get(Guid cid)
    {
        // NOTE: EffectivePrice is deliberately computed in C# AFTER the query,
        // not inside the EF projection. PostgreSQL numeric division yields values
        // with scale up to ~40+ digits (e.g. 149.0000…), and Npgsql cannot convert
        // a scale > 28 numeric into a System.Decimal — it throws OverflowException
        // ("Numeric value does not fit in a System.Decimal"), 500ing this endpoint.
        var row = await _db.Subscriptions.AsNoTracking()
            .Where(s => s.CompanyId == cid && !s.IsDeleted)
            .OrderByDescending(s => s.StartDate)
            .Select(s => new
            {
                s.Id, s.CompanyId, s.PackageId,
                PackageName = s.Package.Name,
                Status = (int)s.Status, s.StartDate, s.EndDate, s.CanceledAt,
                s.CurrentPrice, s.Currency, s.BillingCycle,
                s.DiscountPercentage, s.TaxPercentage,
                s.MaxUsers, s.MaxVehicles, s.MaxDrivers,
                s.CreatedAt
            }).FirstOrDefaultAsync();

        if (row == null)
            return Ok(ApiResponse<object>.Ok(null));

        var result = new
        {
            row.Id, row.CompanyId, row.PackageId, row.PackageName,
            row.Status, row.StartDate, row.EndDate, row.CanceledAt,
            row.CurrentPrice, row.Currency, row.BillingCycle,
            row.DiscountPercentage, row.TaxPercentage,
            EffectivePrice = row.CurrentPrice * (1 - (row.DiscountPercentage ?? 0) / 100m) * (1 + (row.TaxPercentage ?? 0) / 100m),
            row.MaxUsers, row.MaxVehicles, row.MaxDrivers,
            row.CreatedAt
        };

        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse>> Assign(Guid cid, [FromBody] AssignSubscriptionDto dto)
    {
        if (dto.CompanyId != cid)
            return BadRequest(ApiResponse.Fail("MISMATCH", "Company ID in URL and body do not match"));

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == cid && !c.IsDeleted);
        if (company == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Company not found"));

        var package = await _db.Packages.FirstOrDefaultAsync(p => p.Id == dto.PackageId && !p.IsDeleted);
        if (package == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Package not found"));

        // Deactivate existing active subscriptions (preserve history)
        var existing = await _db.Subscriptions
            .Where(s => s.CompanyId == cid && !s.IsDeleted && s.Status == SubscriptionStatus.Active)
            .ToListAsync();
        foreach (var s in existing)
        {
            s.Status = SubscriptionStatus.Canceled;
            s.CanceledAt = DateTime.UtcNow;
        }

        var subscription = new Subscription
        {
            Id = Guid.NewGuid(), CompanyId = cid, PackageId = dto.PackageId,
            Status = SubscriptionStatus.Active, StartDate = dto.StartDate ?? DateTime.UtcNow,
            EndDate = dto.EndDate,
            // Default commercial fields from the package when not supplied, so a
            // minimal {companyId, packageId} payload can never violate NOT NULL.
            CurrentPrice = dto.CurrentPrice ?? package.Price,
            Currency = dto.Currency ?? package.Currency,
            BillingCycle = dto.BillingCycle ?? package.BillingCycle,
            DiscountPercentage = dto.DiscountPercentage, TaxPercentage = dto.TaxPercentage,
            MaxUsers = dto.MaxUsers ?? (package.MaxUsers == -1 ? null : package.MaxUsers),
            MaxVehicles = dto.MaxVehicles ?? (package.MaxVehicles == -1 ? null : package.MaxVehicles),
            MaxDrivers = dto.MaxDrivers ?? (package.MaxDrivers == -1 ? null : package.MaxDrivers),
            TenantId = cid
        };
        _db.Subscriptions.Add(subscription);

        company.SubscriptionId = subscription.Id;
        company.PackageId = dto.PackageId;

        await _db.SaveChangesAsync();

        // Package change alters company-level module access → drop cached permissions
        _permissionService.InvalidateAllCache();

        return Ok(ApiResponse.Ok(message: "Subscription assigned"));
    }

    [HttpPost("renew")]
    public async Task<ActionResult<ApiResponse>> Renew(Guid cid, [FromBody] RenewSubscriptionDto dto)
    {
        var sub = await _db.Subscriptions
            .FirstOrDefaultAsync(s => s.CompanyId == cid && !s.IsDeleted && s.Status == SubscriptionStatus.Active);
        if (sub == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "No active subscription found"));

        sub.EndDate = dto.NewEndDate;
        if (dto.NewPrice.HasValue) sub.CurrentPrice = dto.NewPrice.Value;

        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "Subscription renewed"));
    }

    [HttpDelete]
    public async Task<ActionResult<ApiResponse>> Cancel(Guid cid)
    {
        var sub = await _db.Subscriptions
            .FirstOrDefaultAsync(s => s.CompanyId == cid && !s.IsDeleted && s.Status == SubscriptionStatus.Active);
        if (sub == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "No active subscription found"));

        sub.Status = SubscriptionStatus.Canceled;
        sub.CanceledAt = DateTime.UtcNow;
        sub.EndDate = DateTime.UtcNow;

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == cid);
        if (company != null)
        {
            company.SubscriptionId = null;
            company.PackageId = null;
        }

        await _db.SaveChangesAsync();

        // Package removal alters company-level module access → drop cached permissions
        _permissionService.InvalidateAllCache();

        return Ok(ApiResponse.Ok(message: "Subscription canceled"));
    }
}
