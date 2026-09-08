using Freebuff.Platform.Api.Authorization;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Shared.Extensions;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Controllers;

/// <summary>
/// Internal (authenticated) management of Customer Tracking Link share tokens
/// for one trip: generate, audit, revoke. The ACCESS primitive on the public
/// side is possession of the token — deliberately outside RBAC. Here, only the
/// *management* of links is gated: you must already be able to view the trip
/// (trip.view + trip ownership) to create or revoke its links.
/// </summary>
[ApiController]
[Route("api/v1/trips/{id:guid}")]
[Authorize]
public class TripShareLinksController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly TripShareLinkService _links;
    private readonly AuditLogService _audit;
    private readonly ITenantContext _tenant;

    public TripShareLinksController(ApplicationDbContext db, TripShareLinkService links, AuditLogService audit, ITenantContext tenant)
    {
        _db = db;
        _audit = audit;
        _tenant = tenant;
        _links = links;
    }

    private Task<bool> OwnsTripAsync(Guid id)
        => _db.Trips.AsNoTracking()
            .AnyAsync(t => t.Id == id && !t.IsDeleted && (User.IsSuperAdmin() || t.CompanyId == User.GetTenantId()));

    private async Task<IActionResult?> GuardTripAsync(Guid id)
        => await OwnsTripAsync(id) ? null : NotFound(new ApiResponse<object> { Success = false, Message = "Trip not found." });

    /// <summary>Create a share link; optional explicit expiry in days (default = trip completion + 30 days).</summary>
    [HttpPost("share-links")]
    [RequirePermission("trip.view")]
    public async Task<IActionResult> Create(Guid id, [FromBody] CreateTripShareLinkDto dto)
    {
        var guard = await GuardTripAsync(id);
        if (guard != null) return guard;

        var link = await _links.GenerateAsync(id, User.GetUserIdString(), dto.ExpiresInDays);

        var tripCompanyId = await _db.Trips.AsNoTracking()
            .Where(t => t.Id == id && !t.IsDeleted)
            .Select(t => (Guid?)t.CompanyId)
            .FirstOrDefaultAsync();
        _audit.TryRecord(new AuditLogRecord
        {
            ActorUserId = User.GetUserId(),
            ActorRole = _tenant.UserRole,
            ActorEmail = User.GetEmail(),
            Action = AuditAction.Create,
            ActionCode = "share_link.generated",
            EntityType = EntityType.ShareLink,
            EntityId = link.Id,
            EntityName = tripCompanyId.HasValue ? "tracking link" : null,
            TargetCompanyId = tripCompanyId,
            AfterState = System.Text.Json.JsonSerializer.Serialize(new { expiresAt = link.ExpiresAt }),
            IpAddress = _tenant.IpAddress
        });
        return Ok(new ApiResponse<TripShareLinkDto>
        {
            Success = true,
            Message = "Tracking link created.",
            Data = ToDto(link)
        });
    }

    /// <summary>Audit list of currently active links for the trip (who, when, expiry).</summary>
    [HttpGet("share-links")]
    [RequirePermission("trip.view")]
    public async Task<IActionResult> List(Guid id)
    {
        var guard = await GuardTripAsync(id);
        if (guard != null) return guard;

        var links = await _links.ListForTripAsync(id);
        return Ok(new ApiResponse<List<TripShareLinkDto>>
        {
            Success = true,
            Data = links.Select(ToDto).ToList()
        });
    }

    /// <summary>Revoke a link — takes effect on the next public request (no caching).</summary>
    [HttpPost("share-links/{linkId:guid}/revoke")]
    [RequirePermission("trip.view")]
    public async Task<IActionResult> Revoke(Guid id, Guid linkId)
    {
        var guard = await GuardTripAsync(id);
        if (guard != null) return guard;

        var tripCompanyId = await _db.Trips.AsNoTracking()
            .Where(t => t.Id == id && !t.IsDeleted)
            .Select(t => (Guid?)t.CompanyId)
            .FirstOrDefaultAsync();
        var revoked = await _links.RevokeAsync(id, linkId, User.GetUserIdString());
        if (!revoked) return NotFound(new ApiResponse<object> { Success = false, Message = "Share link not found." });

        _audit.TryRecord(new AuditLogRecord
        {
            ActorUserId = User.GetUserId(),
            ActorRole = _tenant.UserRole,
            ActorEmail = User.GetEmail(),
            Action = AuditAction.Delete,
            ActionCode = "share_link.revoked",
            EntityType = EntityType.ShareLink,
            EntityId = linkId,
            EntityName = tripCompanyId.HasValue ? "tracking link" : null,
            TargetCompanyId = tripCompanyId,
            IpAddress = _tenant.IpAddress
        });
        return Ok(new ApiResponse<object> { Success = true, Message = "Tracking link revoked — the public view is now unavailable." });
    }

    private static TripShareLinkDto ToDto(Domain.Entities.TripShareLink l) => new()
    {
        Id = l.Id,
        TripId = l.TripId,
        Token = l.Token,
        ShareUrl = $"/tracking/{l.Token}",
        ExpiresAt = l.ExpiresAt,
        IsRevoked = l.IsRevoked,
        CreatedBy = l.CreatedByUserId,
        CreatedAt = l.CreatedAt
    };
}