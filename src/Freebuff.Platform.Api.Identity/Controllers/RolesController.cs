using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Api.Identity.Data;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Identity.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class RolesController : ControllerBase
{
    private readonly IdentityDbContext _db;

    public RolesController(IdentityDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<RoleDto>>>> GetAll([FromQuery] PagedRequest filter)
    {
        var query = _db.Roles.AsNoTracking().Where(r => !r.IsDeleted).AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(r => r.Name.Contains(filter.Search));

        var totalCount = await query.CountAsync();
        var items = await query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(r => new RoleDto
            {
                Id = r.Id, Name = r.Name, Description = r.Description,
                CompanyId = r.CompanyId, Status = (int)r.Status, IsSystemRole = r.IsSystemRole
            }).ToListAsync();

        return Ok(ApiResponse<PagedResult<RoleDto>>.Ok(new PagedResult<RoleDto>
        {
            Items = items, TotalCount = totalCount, Page = filter.Page, PageSize = filter.PageSize
        }));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<RoleDto>>> Create([FromBody] CreateRoleDto dto)
    {
        var role = new Role
        {
            Id = Guid.NewGuid(), Name = dto.Name, Description = dto.Description,
            CompanyId = dto.CompanyId, Status = EntityStatus.Active
        };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), ApiResponse<RoleDto>.Ok(new RoleDto
        {
            Id = role.Id, Name = role.Name, Description = role.Description, CompanyId = role.CompanyId
        }));
    }
}
