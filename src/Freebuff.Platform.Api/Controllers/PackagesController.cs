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
            .Select(p => MapToDto(p, p.Subscriptions.Count(s => !s.IsDeleted && s.Status == SubscriptionStatus.Active)))
            .ToListAsync();

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
            .Select(p => MapToDto(p, p.Subscriptions.Count(s => !s.IsDeleted && s.Status == SubscriptionStatus.Active)))
            .FirstOrDefaultAsync();

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
            Id = Guid.NewGuid(),
            Name = dto.Name, Description = dto.Description, ShortDescription = dto.ShortDescription,
            Highlights = dto.Highlights, TermsOfServiceUrl = dto.TermsOfServiceUrl, WelcomeMessage = dto.WelcomeMessage,
            Price = dto.Price, Currency = dto.Currency, BillingCycle = dto.BillingCycle,
            YearlyPrice = dto.YearlyPrice, SetupFee = dto.SetupFee, MinCommitment = dto.MinCommitment,
            DisplayOrder = dto.DisplayOrder, IsDefault = dto.IsDefault, IsCustom = false,
            TrialDays = dto.TrialDays, AllowTrialExtension = dto.AllowTrialExtension,
            MaxTrialExtensions = dto.MaxTrialExtensions, TrialExtensionDays = dto.TrialExtensionDays,
            MaxUsers = dto.MaxUsers, MaxVehicles = dto.MaxVehicles, MaxDrivers = dto.MaxDrivers,
            MaxTripsPerDay = dto.MaxTripsPerDay, MaxRoutes = dto.MaxRoutes, MaxReportsPerDay = dto.MaxReportsPerDay,
            StorageLimitMb = dto.StorageLimitMb, MaxApiCallsPerDay = dto.MaxApiCallsPerDay,
            MaxTrackingDevices = dto.MaxTrackingDevices, MaxAlertRules = dto.MaxAlertRules,
            MaxGeofences = dto.MaxGeofences, MaxDocuments = dto.MaxDocuments, MaxNotificationsPerDay = dto.MaxNotificationsPerDay,
            OveragePricePerUser = dto.OveragePricePerUser, OveragePricePerVehicle = dto.OveragePricePerVehicle,
            OveragePricePerDriver = dto.OveragePricePerDriver, OveragePricePerTrip = dto.OveragePricePerTrip,
            OveragePricePerApiCall = dto.OveragePricePerApiCall, OveragePricePerGbStorage = dto.OveragePricePerGbStorage,
            SupportLevel = dto.SupportLevel, SlaUptimePercent = dto.SlaUptimePercent,
            SupportHours = dto.SupportHours, SupportContactEmail = dto.SupportContactEmail, SupportContactPhone = dto.SupportContactPhone,
            ResponseTimeHours = dto.ResponseTimeHours, ResolutionTimeHours = dto.ResolutionTimeHours,
            EnableLiveTracking = dto.EnableLiveTracking, EnableGeofencing = dto.EnableGeofencing,
            EnableAlerts = dto.EnableAlerts, EnableReports = dto.EnableReports,
            EnableFuelMonitoring = dto.EnableFuelMonitoring, EnableMaintenance = dto.EnableMaintenance,
            EnableRouteOptimization = dto.EnableRouteOptimization, EnableProofOfDelivery = dto.EnableProofOfDelivery,
            EnableCctv = dto.EnableCctv, EnableSmsNotifications = dto.EnableSmsNotifications,
            EnableEmailNotifications = dto.EnableEmailNotifications, EnableWebhookIntegrations = dto.EnableWebhookIntegrations,
            EnableApiAccess = dto.EnableApiAccess, EnableBulkImport = dto.EnableBulkImport,
            EnableExport = dto.EnableExport, EnableCustomFields = dto.EnableCustomFields,
            EnableMultiCompany = dto.EnableMultiCompany, EnableAuditLog = dto.EnableAuditLog,
            Status = EntityStatus.Active
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
        if (dto.ShortDescription != null) package.ShortDescription = dto.ShortDescription;
        if (dto.Highlights != null) package.Highlights = dto.Highlights;
        if (dto.TermsOfServiceUrl != null) package.TermsOfServiceUrl = dto.TermsOfServiceUrl;
        if (dto.WelcomeMessage != null) package.WelcomeMessage = dto.WelcomeMessage;
        if (dto.Price.HasValue) package.Price = dto.Price.Value;
        if (dto.Currency != null) package.Currency = dto.Currency;
        if (dto.BillingCycle != null) package.BillingCycle = dto.BillingCycle;
        if (dto.YearlyPrice.HasValue) package.YearlyPrice = dto.YearlyPrice;
        if (dto.SetupFee.HasValue) package.SetupFee = dto.SetupFee;
        if (dto.MinCommitment.HasValue) package.MinCommitment = dto.MinCommitment;
        if (dto.DisplayOrder.HasValue) package.DisplayOrder = dto.DisplayOrder.Value;
        if (dto.IsDefault.HasValue) package.IsDefault = dto.IsDefault.Value;
        if (dto.Status.HasValue) package.Status = (EntityStatus)dto.Status.Value;
        if (dto.TrialDays.HasValue) package.TrialDays = dto.TrialDays.Value;
        if (dto.AllowTrialExtension.HasValue) package.AllowTrialExtension = dto.AllowTrialExtension.Value;
        if (dto.MaxTrialExtensions.HasValue) package.MaxTrialExtensions = dto.MaxTrialExtensions.Value;
        if (dto.TrialExtensionDays.HasValue) package.TrialExtensionDays = dto.TrialExtensionDays.Value;
        if (dto.MaxUsers.HasValue) package.MaxUsers = dto.MaxUsers.Value;
        if (dto.MaxVehicles.HasValue) package.MaxVehicles = dto.MaxVehicles.Value;
        if (dto.MaxDrivers.HasValue) package.MaxDrivers = dto.MaxDrivers.Value;
        if (dto.MaxTripsPerDay.HasValue) package.MaxTripsPerDay = dto.MaxTripsPerDay.Value;
        if (dto.MaxRoutes.HasValue) package.MaxRoutes = dto.MaxRoutes.Value;
        if (dto.MaxReportsPerDay.HasValue) package.MaxReportsPerDay = dto.MaxReportsPerDay.Value;
        if (dto.StorageLimitMb.HasValue) package.StorageLimitMb = dto.StorageLimitMb.Value;
        if (dto.MaxApiCallsPerDay.HasValue) package.MaxApiCallsPerDay = dto.MaxApiCallsPerDay.Value;
        if (dto.MaxTrackingDevices.HasValue) package.MaxTrackingDevices = dto.MaxTrackingDevices.Value;
        if (dto.MaxAlertRules.HasValue) package.MaxAlertRules = dto.MaxAlertRules.Value;
        if (dto.MaxGeofences.HasValue) package.MaxGeofences = dto.MaxGeofences.Value;
        if (dto.MaxDocuments.HasValue) package.MaxDocuments = dto.MaxDocuments.Value;
        if (dto.MaxNotificationsPerDay.HasValue) package.MaxNotificationsPerDay = dto.MaxNotificationsPerDay.Value;
        if (dto.OveragePricePerUser.HasValue) package.OveragePricePerUser = dto.OveragePricePerUser.Value;
        if (dto.OveragePricePerVehicle.HasValue) package.OveragePricePerVehicle = dto.OveragePricePerVehicle.Value;
        if (dto.OveragePricePerDriver.HasValue) package.OveragePricePerDriver = dto.OveragePricePerDriver.Value;
        if (dto.OveragePricePerTrip.HasValue) package.OveragePricePerTrip = dto.OveragePricePerTrip.Value;
        if (dto.OveragePricePerApiCall.HasValue) package.OveragePricePerApiCall = dto.OveragePricePerApiCall.Value;
        if (dto.OveragePricePerGbStorage.HasValue) package.OveragePricePerGbStorage = dto.OveragePricePerGbStorage.Value;
        if (dto.SupportLevel != null) package.SupportLevel = dto.SupportLevel;
        if (dto.SlaUptimePercent.HasValue) package.SlaUptimePercent = dto.SlaUptimePercent.Value;
        if (dto.SupportHours != null) package.SupportHours = dto.SupportHours;
        if (dto.SupportContactEmail != null) package.SupportContactEmail = dto.SupportContactEmail;
        if (dto.SupportContactPhone != null) package.SupportContactPhone = dto.SupportContactPhone;
        if (dto.ResponseTimeHours.HasValue) package.ResponseTimeHours = dto.ResponseTimeHours.Value;
        if (dto.ResolutionTimeHours.HasValue) package.ResolutionTimeHours = dto.ResolutionTimeHours.Value;
        if (dto.EnableLiveTracking.HasValue) package.EnableLiveTracking = dto.EnableLiveTracking.Value;
        if (dto.EnableGeofencing.HasValue) package.EnableGeofencing = dto.EnableGeofencing.Value;
        if (dto.EnableAlerts.HasValue) package.EnableAlerts = dto.EnableAlerts.Value;
        if (dto.EnableReports.HasValue) package.EnableReports = dto.EnableReports.Value;
        if (dto.EnableFuelMonitoring.HasValue) package.EnableFuelMonitoring = dto.EnableFuelMonitoring.Value;
        if (dto.EnableMaintenance.HasValue) package.EnableMaintenance = dto.EnableMaintenance.Value;
        if (dto.EnableRouteOptimization.HasValue) package.EnableRouteOptimization = dto.EnableRouteOptimization.Value;
        if (dto.EnableProofOfDelivery.HasValue) package.EnableProofOfDelivery = dto.EnableProofOfDelivery.Value;
        if (dto.EnableCctv.HasValue) package.EnableCctv = dto.EnableCctv.Value;
        if (dto.EnableSmsNotifications.HasValue) package.EnableSmsNotifications = dto.EnableSmsNotifications.Value;
        if (dto.EnableEmailNotifications.HasValue) package.EnableEmailNotifications = dto.EnableEmailNotifications.Value;
        if (dto.EnableWebhookIntegrations.HasValue) package.EnableWebhookIntegrations = dto.EnableWebhookIntegrations.Value;
        if (dto.EnableApiAccess.HasValue) package.EnableApiAccess = dto.EnableApiAccess.Value;
        if (dto.EnableBulkImport.HasValue) package.EnableBulkImport = dto.EnableBulkImport.Value;
        if (dto.EnableExport.HasValue) package.EnableExport = dto.EnableExport.Value;
        if (dto.EnableCustomFields.HasValue) package.EnableCustomFields = dto.EnableCustomFields.Value;
        if (dto.EnableMultiCompany.HasValue) package.EnableMultiCompany = dto.EnableMultiCompany.Value;
        if (dto.EnableAuditLog.HasValue) package.EnableAuditLog = dto.EnableAuditLog.Value;

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
        Id = p.Id, Name = p.Name, Description = p.Description, ShortDescription = p.ShortDescription,
        Highlights = p.Highlights, TermsOfServiceUrl = p.TermsOfServiceUrl, WelcomeMessage = p.WelcomeMessage,
        Price = p.Price, Currency = p.Currency, BillingCycle = p.BillingCycle,
        YearlyPrice = p.YearlyPrice, SetupFee = p.SetupFee, MinCommitment = p.MinCommitment,
        Status = (int)p.Status, DisplayOrder = p.DisplayOrder,
        IsDefault = p.IsDefault, IsCustom = p.IsCustom,
        TrialDays = p.TrialDays, AllowTrialExtension = p.AllowTrialExtension,
        MaxTrialExtensions = p.MaxTrialExtensions, TrialExtensionDays = p.TrialExtensionDays,
        MaxUsers = p.MaxUsers, MaxVehicles = p.MaxVehicles, MaxDrivers = p.MaxDrivers,
        MaxTripsPerDay = p.MaxTripsPerDay, MaxRoutes = p.MaxRoutes, MaxReportsPerDay = p.MaxReportsPerDay,
        StorageLimitMb = p.StorageLimitMb, MaxApiCallsPerDay = p.MaxApiCallsPerDay,
        MaxTrackingDevices = p.MaxTrackingDevices, MaxAlertRules = p.MaxAlertRules,
        MaxGeofences = p.MaxGeofences, MaxDocuments = p.MaxDocuments, MaxNotificationsPerDay = p.MaxNotificationsPerDay,
        OveragePricePerUser = p.OveragePricePerUser, OveragePricePerVehicle = p.OveragePricePerVehicle,
        OveragePricePerDriver = p.OveragePricePerDriver, OveragePricePerTrip = p.OveragePricePerTrip,
        OveragePricePerApiCall = p.OveragePricePerApiCall, OveragePricePerGbStorage = p.OveragePricePerGbStorage,
        SupportLevel = p.SupportLevel, SlaUptimePercent = p.SlaUptimePercent,
        SupportHours = p.SupportHours, SupportContactEmail = p.SupportContactEmail, SupportContactPhone = p.SupportContactPhone,
        ResponseTimeHours = p.ResponseTimeHours, ResolutionTimeHours = p.ResolutionTimeHours,
        EnableLiveTracking = p.EnableLiveTracking, EnableGeofencing = p.EnableGeofencing,
        EnableAlerts = p.EnableAlerts, EnableReports = p.EnableReports,
        EnableFuelMonitoring = p.EnableFuelMonitoring, EnableMaintenance = p.EnableMaintenance,
        EnableRouteOptimization = p.EnableRouteOptimization, EnableProofOfDelivery = p.EnableProofOfDelivery,
        EnableCctv = p.EnableCctv, EnableSmsNotifications = p.EnableSmsNotifications,
        EnableEmailNotifications = p.EnableEmailNotifications, EnableWebhookIntegrations = p.EnableWebhookIntegrations,
        EnableApiAccess = p.EnableApiAccess, EnableBulkImport = p.EnableBulkImport,
        EnableExport = p.EnableExport, EnableCustomFields = p.EnableCustomFields,
        EnableMultiCompany = p.EnableMultiCompany, EnableAuditLog = p.EnableAuditLog,
        ActiveSubscriptions = activeSubscriptions, CreatedAt = p.CreatedAt
    };
}
