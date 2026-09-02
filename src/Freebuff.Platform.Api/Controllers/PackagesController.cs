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
[Route("api/v1/admin/packages")]
[Authorize(Roles = "SuperAdmin")]
public class PackagesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public PackagesController(ApplicationDbContext db) => _db = db;

    // ── List all packages ────────────────────────────────
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<PackageDto>>>> GetAll([FromQuery] PagedRequest filter)
    {
        var query = _db.Packages.AsNoTracking().Where(p => !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(p => p.Name.Contains(filter.Search) || (p.Description != null && p.Description.Contains(filter.Search)));

        query = filter.SortBy?.ToLower() switch
        {
            "price" => filter.SortDescending ? query.OrderByDescending(p => p.Price) : query.OrderBy(p => p.Price),
            "name" => filter.SortDescending ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name),
            _ => query.OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name)
        };

        var total = await query.CountAsync();
        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(p => new PackageDto
            {
                Id = p.Id, Name = p.Name, Description = p.Description,
                Price = p.Price, Currency = p.Currency, BillingCycle = p.BillingCycle,
                Status = (int)p.Status, DisplayOrder = p.DisplayOrder,
                IsDefault = p.IsDefault, IsCustom = p.IsCustom,
                MaxUsers = p.MaxUsers, MaxVehicles = p.MaxVehicles, MaxDrivers = p.MaxDrivers,
                StorageLimitMb = p.StorageLimitMb, MaxApiCallsPerDay = p.MaxApiCallsPerDay,
                MaxTrackingDevices = p.MaxTrackingDevices, MaxAlertRules = p.MaxAlertRules,
                MaxGeofences = p.MaxGeofences,
                ActiveSubscriptions = p.Subscriptions.Count(s => !s.IsDeleted && s.Status == SubscriptionStatus.Active),
                CreatedAt = p.CreatedAt
            }).ToListAsync();

        return Ok(ApiResponse<PagedResult<PackageDto>>.Ok(new PagedResult<PackageDto>
        {
            Items = items, TotalCount = total, Page = filter.Page, PageSize = filter.PageSize
        }));
    }

    // ── Get single package ───────────────────────────────
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<PackageDto>>> GetById(Guid id)
    {
        var p = await _db.Packages.AsNoTracking()
            .Where(p => p.Id == id && !p.IsDeleted)
            .Select(p => new PackageDto
            {
                Id = p.Id, Name = p.Name, Description = p.Description,
                Price = p.Price, Currency = p.Currency, BillingCycle = p.BillingCycle,
                Status = (int)p.Status, DisplayOrder = p.DisplayOrder,
                IsDefault = p.IsDefault, IsCustom = p.IsCustom,
                MaxUsers = p.MaxUsers, MaxVehicles = p.MaxVehicles, MaxDrivers = p.MaxDrivers,
                StorageLimitMb = p.StorageLimitMb, MaxApiCallsPerDay = p.MaxApiCallsPerDay,
                MaxTrackingDevices = p.MaxTrackingDevices, MaxAlertRules = p.MaxAlertRules,
                MaxGeofences = p.MaxGeofences,
                ActiveSubscriptions = p.Subscriptions.Count(s => !s.IsDeleted && s.Status == SubscriptionStatus.Active),
                CreatedAt = p.CreatedAt
            }).FirstOrDefaultAsync();

        if (p == null) return NotFound(ApiResponse<PackageDto>.Fail("NOT_FOUND", "Package not found"));
        return Ok(ApiResponse<PackageDto>.Ok(p));
    }

    // ── Create package ───────────────────────────────────
    [HttpPost]
    public async Task<ActionResult<ApiResponse<PackageDto>>> Create([FromBody] CreatePackageDto dto)
    {
        if (await _db.Packages.AnyAsync(p => p.Name == dto.Name && !p.IsDeleted))
            return BadRequest(ApiResponse.Fail("DUPLICATE", "A package with this name already exists"));

        var package = new Package
        {
            Id = Guid.NewGuid(), Name = dto.Name, Description = dto.Description,
            Price = dto.Price, Currency = dto.Currency, BillingCycle = dto.BillingCycle,
            DisplayOrder = dto.DisplayOrder, IsDefault = dto.IsDefault, IsCustom = false,
            MaxUsers = dto.MaxUsers, MaxVehicles = dto.MaxVehicles, MaxDrivers = dto.MaxDrivers,
            StorageLimitMb = dto.StorageLimitMb, MaxApiCallsPerDay = dto.MaxApiCallsPerDay,
            MaxTrackingDevices = dto.MaxTrackingDevices, MaxAlertRules = dto.MaxAlertRules,
            MaxGeofences = dto.MaxGeofences, Status = EntityStatus.Active
        };
        _db.Packages.Add(package);
        await _db.SaveChangesAsync();

        var result = MapToDto(package, 0);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<PackageDto>.Ok(result));
    }

    // ── Update package ───────────────────────────────────
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<PackageDto>>> Update(Guid id, [FromBody] UpdatePackageDto dto)
    {
        var package = await _db.Packages.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
        if (package == null) return NotFound(ApiResponse<PackageDto>.Fail("NOT_FOUND", "Package not found"));

        if (dto.Name != null) package.Name = dto.Name;
        if (dto.Description != null) package.Description = dto.Description;
        if (dto.Price.HasValue) package.Price = dto.Price.Value;
        if (dto.Currency != null) package.Currency = dto.Currency;
        if (dto.BillingCycle != null) package.BillingCycle = dto.BillingCycle;
        if (dto.DisplayOrder.HasValue) package.DisplayOrder = dto.DisplayOrder.Value;
        if (dto.IsDefault.HasValue) package.IsDefault = dto.IsDefault.Value;
        if (dto.MaxUsers.HasValue) package.MaxUsers = dto.MaxUsers.Value;
        if (dto.MaxVehicles.HasValue) package.MaxVehicles = dto.MaxVehicles.Value;
        if (dto.MaxDrivers.HasValue) package.MaxDrivers = dto.MaxDrivers.Value;
        if (dto.StorageLimitMb.HasValue) package.StorageLimitMb = dto.StorageLimitMb.Value;
        if (dto.MaxApiCallsPerDay.HasValue) package.MaxApiCallsPerDay = dto.MaxApiCallsPerDay.Value;
        if (dto.MaxTrackingDevices.HasValue) package.MaxTrackingDevices = dto.MaxTrackingDevices.Value;
        if (dto.MaxAlertRules.HasValue) package.MaxAlertRules = dto.MaxAlertRules.Value;
        if (dto.MaxGeofences.HasValue) package.MaxGeofences = dto.MaxGeofences.Value;
        if (dto.Status.HasValue) package.Status = (EntityStatus)dto.Status.Value;

        await _db.SaveChangesAsync();

        var subCount = await _db.Subscriptions.CountAsync(s => s.PackageId == id && !s.IsDeleted && s.Status == SubscriptionStatus.Active);
        return Ok(ApiResponse<PackageDto>.Ok(MapToDto(package, subCount)));
    }

    // ── Delete package ───────────────────────────────────
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id)
    {
        var package = await _db.Packages.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
        if (package == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Package not found"));

        var hasActiveSubscriptions = await _db.Subscriptions.AnyAsync(s => s.PackageId == id && !s.IsDeleted && s.Status == SubscriptionStatus.Active);
        if (hasActiveSubscriptions)
            return BadRequest(ApiResponse.Fail("HAS_SUBSCRIPTIONS", "Cannot delete a package with active subscriptions. Remove all subscriptions first."));

        package.IsDeleted = true;
        package.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "Package deleted"));
    }

    private static PackageDto MapToDto(Package p, int activeSubscriptions) => new()
    {
        Id = p.Id, Name = p.Name, Description = p.Description,
        Price = p.Price, Currency = p.Currency, BillingCycle = p.BillingCycle,
        Status = (int)p.Status, DisplayOrder = p.DisplayOrder,
        IsDefault = p.IsDefault, IsCustom = p.IsCustom,
        MaxUsers = p.MaxUsers, MaxVehicles = p.MaxVehicles, MaxDrivers = p.MaxDrivers,
        StorageLimitMb = p.StorageLimitMb, MaxApiCallsPerDay = p.MaxApiCallsPerDay,
        MaxTrackingDevices = p.MaxTrackingDevices, MaxAlertRules = p.MaxAlertRules,
        MaxGeofences = p.MaxGeofences, ActiveSubscriptions = activeSubscriptions,
        CreatedAt = p.CreatedAt
    };
}
