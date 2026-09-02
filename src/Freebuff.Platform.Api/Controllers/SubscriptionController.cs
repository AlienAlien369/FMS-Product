using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
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
    public SubscriptionController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<object>>> Get(Guid cid)
    {
        var sub = await _db.Subscriptions.AsNoTracking()
            .Include(s => s.Package)
            .Where(s => s.CompanyId == cid && !s.IsDeleted)
            .OrderByDescending(s => s.StartDate)
            .Select(s => new
            {
                s.Id, s.CompanyId, s.PackageId,
                PackageName = s.Package.Name,
                Status = (int)s.Status, s.StartDate, s.EndDate, s.CanceledAt,
                s.CurrentPrice, s.Currency, s.BillingCycle,
                s.DiscountPercentage, s.TaxPercentage,
                EffectivePrice = s.CurrentPrice * (1 - (s.DiscountPercentage ?? 0) / 100) * (1 + (s.TaxPercentage ?? 0) / 100),
                s.MaxUsers, s.MaxVehicles, s.MaxDrivers,
                s.CreatedAt
            }).FirstOrDefaultAsync();

        return Ok(ApiResponse<object>.Ok(sub));
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
            Status = SubscriptionStatus.Active, StartDate = dto.StartDate,
            EndDate = dto.EndDate, CurrentPrice = dto.CurrentPrice,
            Currency = dto.Currency, BillingCycle = dto.BillingCycle,
            DiscountPercentage = dto.DiscountPercentage, TaxPercentage = dto.TaxPercentage,
            MaxUsers = dto.MaxUsers, MaxVehicles = dto.MaxVehicles, MaxDrivers = dto.MaxDrivers,
            TenantId = cid
        };
        _db.Subscriptions.Add(subscription);

        company.SubscriptionId = subscription.Id;
        company.PackageId = dto.PackageId;

        await _db.SaveChangesAsync();
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
        return Ok(ApiResponse.Ok(message: "Subscription canceled"));
    }
}
