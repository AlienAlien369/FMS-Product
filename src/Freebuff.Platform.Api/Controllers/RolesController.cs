using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Shared.Extensions;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class RolesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IPermissionService _permissionService;
    public RolesController(ApplicationDbContext db, IPermissionService permissionService)
    {
        _db = db;
        _permissionService = permissionService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<object>>>> GetAll([FromQuery] PagedRequest filter)
    {
        var tenantId = User.GetTenantId();
        var isSuperAdmin = User.IsSuperAdmin();
        var query = _db.Roles.AsNoTracking()
            .Where(r => !r.IsDeleted && (isSuperAdmin || r.CompanyId == tenantId));

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(r => r.Name.Contains(filter.Search));

        query = query.OrderBy(r => r.DisplayOrder).ThenBy(r => r.Name);

        var total = await query.CountAsync();
        var items = await query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(r => new
            {
                r.Id, r.Name, r.Description, r.IsSystemRole,
                Status = (int)r.Status, r.DisplayOrder,
                UserCount = r.UserRoles.Count(ur => !ur.IsDeleted),
                PermissionCount = r.RolePermissions.Count(rp => !rp.IsDeleted)
            }).ToListAsync();

        return Ok(ApiResponse<PagedResult<object>>.Ok(new PagedResult<object>
        {
            Items = items.Cast<object>().ToList(),
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> GetById(Guid id)
    {
        var tenantId = User.GetTenantId();
        var isSuperAdmin = User.IsSuperAdmin();
        var role = await _db.Roles.AsNoTracking()
            .Where(r => r.Id == id && !r.IsDeleted && (isSuperAdmin || r.CompanyId == tenantId))
            .Select(r => new
            {
                r.Id, r.Name, r.Description, r.IsSystemRole,
                Status = (int)r.Status, r.CompanyId
            })
            .FirstOrDefaultAsync();

        if (role == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Role not found"));
        return Ok(ApiResponse<object>.Ok(role));
    }

    [HttpGet("{id:guid}/permissions")]
    public async Task<ActionResult<ApiResponse<object>>> GetPermissions(Guid id)
    {
        var tenantId = User.GetTenantId();
        var isSuperAdmin = User.IsSuperAdmin();
        var permissions = await _db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == id && !rp.IsDeleted && (isSuperAdmin || rp.Role.CompanyId == tenantId))
            .Select(rp => new
            {
                rp.Id,
                rp.PermissionId,
                rp.Permission.Code,
                rp.Permission.Name,
                rp.Permission.Module,
                Action = rp.Permission.Action.ToString()
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(permissions));
    }

    // ── My Permissions (for hierarchy check) ──────────────
    [HttpGet("my-permissions")]
    public async Task<ActionResult<ApiResponse<object>>> GetMyPermissions()
    {
        if (User.IsSuperAdmin())
        {
            // SuperAdmin has all permissions
            var allPerms = await _db.Permissions.AsNoTracking()
                .Where(p => !p.IsDeleted)
                .Select(p => p.Id).ToListAsync();
            return Ok(ApiResponse<object>.Ok(new { permissionIds = allPerms, isSuperAdmin = true }));
        }

        var userId = User.GetUserId();
        var myPermIds = await _db.UserRoles
            .Where(ur => ur.UserId == userId && !ur.IsDeleted)
            .SelectMany(ur => ur.Role.RolePermissions)
            .Where(rp => !rp.IsDeleted)
            .Select(rp => rp.PermissionId)
            .Distinct()
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { permissionIds = myPermIds, isSuperAdmin = false }));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<object>>> Create([FromBody] CreateRoleDto dto)
    {
        var tenantId = User.GetTenantId();

        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Description = dto.Description,
            CompanyId = tenantId,
            Status = EntityStatus.Active,
            IsSystemRole = false
        };
        _db.Roles.Add(role);

        if (dto.PermissionIds?.Count > 0)
        {
            // Company Admin can only assign permissions the company is entitled to
            var allowedPermCodes = await _permissionService.GetCompanyAllowedPermissionsAsync(tenantId);

            // For non-SuperAdmin, also intersect with user's own permissions
            if (!User.IsSuperAdmin())
            {
                var myUserId = User.GetUserId();
                var myPerms = await _permissionService.GetEffectivePermissionsAsync(myUserId, tenantId);
                allowedPermCodes = allowedPermCodes.Intersect(myPerms).ToHashSet();
            }

            // Resolve permission IDs from allowed codes
            var allowedPermIds = await _db.Permissions
                .Where(p => allowedPermCodes.Contains(p.Code) && !p.IsDeleted)
                .Select(p => p.Id)
                .ToListAsync();
            var finalIds = dto.PermissionIds.Where(p => allowedPermIds.Contains(p)).ToList();

            foreach (var permId in finalIds)
            {
                _db.RolePermissions.Add(new RolePermission
                {
                    Id = Guid.NewGuid(),
                    RoleId = role.Id,
                    PermissionId = permId,
                    TenantId = tenantId
                });
            }
        }

        await _db.SaveChangesAsync();

        // Audit log: role created
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = User.GetUserId(),
            UserName = User.GetEmail(),
            Action = AuditAction.Create,
            EntityType = EntityType.Role,
            EntityId = role.Id,
            EntityName = role.Name,
            NewValues = System.Text.Json.JsonSerializer.Serialize(new { role.Name, role.Description, PermissionCount = dto.PermissionIds?.Count ?? 0 })
        });
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = role.Id },
            ApiResponse<object>.Ok(new { role.Id, role.Name, role.Description }));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> Update(Guid id, [FromBody] UpdateRoleDto dto)
    {
        var tenantId = User.GetTenantId();
        var isSuperAdmin = User.IsSuperAdmin();
        var role = await _db.Roles
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted && (isSuperAdmin || r.CompanyId == tenantId));

        if (role == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Role not found"));
        if (role.IsSystemRole) return BadRequest(ApiResponse.Fail("FORBIDDEN", "System roles cannot be modified"));

        if (dto.Name != null) role.Name = dto.Name;
        if (dto.Description != null) role.Description = dto.Description;

        if (dto.PermissionIds != null)
        {
            // Hard-delete old role-permissions immediately via SQL,
            // bypassing the change tracker to avoid unique-constraint violations
            await _db.RolePermissions
                .Where(rp => rp.RoleId == id && !rp.IsDeleted)
                .ExecuteDeleteAsync();

            // Company Admin can only assign permissions the company is entitled to
            var allowedPermCodes = await _permissionService.GetCompanyAllowedPermissionsAsync(tenantId);

            // For non-SuperAdmin, also intersect with user's own permissions
            if (!User.IsSuperAdmin())
            {
                var userId = User.GetUserId();
                var myPerms = await _permissionService.GetEffectivePermissionsAsync(userId, tenantId);
                allowedPermCodes = allowedPermCodes.Intersect(myPerms).ToHashSet();
            }

            var allowedPermIds = await _db.Permissions
                .Where(p => allowedPermCodes.Contains(p.Code) && !p.IsDeleted)
                .Select(p => p.Id)
                .ToListAsync();
            var finalIds = dto.PermissionIds.Where(p => allowedPermIds.Contains(p)).ToList();

            foreach (var permId in finalIds)
            {
                _db.RolePermissions.Add(new RolePermission
                {
                    Id = Guid.NewGuid(),
                    RoleId = id,
                    PermissionId = permId,
                    TenantId = tenantId
                });
            }
        }

        await _db.SaveChangesAsync();

        // Audit log: role updated
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = User.GetUserId(),
            UserName = User.GetEmail(),
            Action = AuditAction.Update,
            EntityType = EntityType.Role,
            EntityId = id,
            EntityName = role.Name,
            NewValues = System.Text.Json.JsonSerializer.Serialize(new { role.Name, role.Description, PermissionCount = dto.PermissionIds?.Count ?? 0 })
        });
        await _db.SaveChangesAsync();

        return Ok(ApiResponse.Ok(message: "Role updated"));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id)
    {
        var tenantId = User.GetTenantId();
        var isSuperAdmin = User.IsSuperAdmin();
        var role = await _db.Roles
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted && (isSuperAdmin || r.CompanyId == tenantId));

        if (role == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Role not found"));
        if (role.IsSystemRole) return BadRequest(ApiResponse.Fail("FORBIDDEN", "System roles cannot be deleted"));

        var roleName = role.Name;
        role.IsDeleted = true;
        role.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Audit log: role deleted
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = User.GetUserId(),
            UserName = User.GetEmail(),
            Action = AuditAction.Delete,
            EntityType = EntityType.Role,
            EntityId = id,
            EntityName = roleName
        });
        await _db.SaveChangesAsync();

        return Ok(ApiResponse.Ok(message: "Role deleted"));
    }
}
