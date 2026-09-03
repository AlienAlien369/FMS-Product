using Freebuff.Platform.Api.Authorization;
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
    [RequirePermission("role.view")]
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
    [RequirePermission("role.view")]
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
    [RequirePermission("role.view")]
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

    [HttpGet("my-permissions")]
    public async Task<ActionResult<ApiResponse<object>>> GetMyPermissions()
    {
        if (User.IsSuperAdmin())
        {
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
    [RequirePermission("role.create")]
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
            var allowedPermCodes = await _permissionService.GetCompanyAllowedPermissionsAsync(tenantId);
            if (!User.IsSuperAdmin())
            {
                var myUserId = User.GetUserId();
                var myPerms = await _permissionService.GetEffectivePermissionsAsync(myUserId, tenantId);
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
                    RoleId = role.Id,
                    PermissionId = permId,
                    TenantId = tenantId
                });
            }
        }

        await _db.SaveChangesAsync();

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

        // Invalidate all permission caches for this tenant
        _permissionService.InvalidateAllCache();

        return CreatedAtAction(nameof(GetById), new { id = role.Id },
            ApiResponse<object>.Ok(new { role.Id, role.Name, role.Description }));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("role.edit")]
    public async Task<ActionResult<ApiResponse>> Update(Guid id, [FromBody] UpdateRoleDto dto)
    {
        var tenantId = User.GetTenantId();
        var isSuperAdmin = User.IsSuperAdmin();

        // Step 1: Validate the role exists and user has access
        var roleExists = await _db.Roles.AsNoTracking()
            .Where(r => r.Id == id && !r.IsDeleted && (isSuperAdmin || r.CompanyId == tenantId))
            .Select(r => new { r.IsSystemRole, r.Name, r.CompanyId })
            .FirstOrDefaultAsync();

        if (roleExists == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Role not found"));
        if (roleExists.IsSystemRole && !isSuperAdmin)
            return BadRequest(ApiResponse.Fail("FORBIDDEN", "System roles cannot be modified by company administrators"));

        // Step 2: Resolve allowed permissions BEFORE touching the tracker
        List<Guid> finalPermIds = new();
        if (dto.PermissionIds != null)
        {
            var allowedPermCodes = await _permissionService.GetCompanyAllowedPermissionsAsync(tenantId);
            if (!isSuperAdmin)
            {
                var userId = User.GetUserId();
                var myPerms = await _permissionService.GetEffectivePermissionsAsync(userId, tenantId);
                allowedPermCodes = allowedPermCodes.Intersect(myPerms).ToHashSet();
            }
            var allowedPermIds = await _db.Permissions
                .Where(p => allowedPermCodes.Contains(p.Code) && !p.IsDeleted)
                .Select(p => p.Id)
                .ToListAsync();
            finalPermIds = dto.PermissionIds.Where(p => allowedPermIds.Contains(p)).ToList();
        }

        // Step 3: Clear tracker to start clean — avoids stale entity conflicts
        _db.ChangeTracker.Clear();

        // Step 4: Replace role-permissions via raw SQL (bypasses change tracker entirely)
        if (dto.PermissionIds != null)
        {
            // Delete old permissions
            var roleIdParam = new Npgsql.NpgsqlParameter("roleId", id);
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM \"RolePermissions\" WHERE \"RoleId\" = @roleId AND \"IsDeleted\" = false",
                roleIdParam);

            // Insert new permissions in bulk
            if (finalPermIds.Count > 0)
            {
                var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fffzzz");
                var values = string.Join(",",
                    finalPermIds.Select(pid => $"('{Guid.NewGuid()}', '{id}', '{pid}', '{tenantId}', '{now}', '{now}', false, 0)"));
#pragma warning disable EF1002
                await _db.Database.ExecuteSqlRawAsync(
                    $"INSERT INTO \"RolePermissions\" (\"Id\", \"RoleId\", \"PermissionId\", \"TenantId\", \"CreatedAt\", \"UpdatedAt\", \"IsDeleted\", \"Version\") VALUES {values}",
                    Array.Empty<object>());
#pragma warning restore EF1002
            }
        }

        // Step 5: Update role name/description via raw SQL
        var nameParam = new Npgsql.NpgsqlParameter("name", dto.Name ?? roleExists.Name);
        var descParam = new Npgsql.NpgsqlParameter("description", (object?)dto.Description ?? DBNull.Value);
        var roleIdParam2 = new Npgsql.NpgsqlParameter("roleId", id);
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE \"Roles\" SET \"Name\" = @name, \"Description\" = @description, \"UpdatedAt\" = now() WHERE \"Id\" = @roleId",
            nameParam, descParam, roleIdParam2);

        // Step 6: Audit log
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = User.GetUserId(),
            UserName = User.GetEmail(),
            Action = AuditAction.Update,
            EntityType = EntityType.Role,
            EntityId = id,
            EntityName = dto.Name ?? roleExists.Name,
            NewValues = System.Text.Json.JsonSerializer.Serialize(new { Name = dto.Name, Description = dto.Description, PermissionCount = dto.PermissionIds?.Count ?? 0 })
        });
        await _db.SaveChangesAsync();

        // Invalidate all permission caches for this tenant
        _permissionService.InvalidateAllCache();

        return Ok(ApiResponse.Ok(message: "Role updated"));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("role.delete")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id)
    {
        var tenantId = User.GetTenantId();
        var isSuperAdmin = User.IsSuperAdmin();
        var role = await _db.Roles
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted && (isSuperAdmin || r.CompanyId == tenantId));

        if (role == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Role not found"));
        if (role.IsSystemRole && !isSuperAdmin)
            return BadRequest(ApiResponse.Fail("FORBIDDEN", "System roles cannot be deleted by company administrators"));

        var roleName = role.Name;
        role.IsDeleted = true;
        role.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

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

        // Invalidate all permission caches for this tenant
        _permissionService.InvalidateAllCache();

        return Ok(ApiResponse.Ok(message: "Role deleted"));
    }
}
