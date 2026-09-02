using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Shared.Extensions;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public UsersController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<object>>>> GetAll([FromQuery] PagedRequest filter)
    {
        var tenantId = User.GetTenantId();
        var isSuperAdmin = User.IsSuperAdmin();

        var query = _db.Users.AsNoTracking()
            .Where(u => !u.IsDeleted && (isSuperAdmin || u.CompanyId == tenantId));

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(u => u.Email.Contains(filter.Search)
                || u.FirstName.Contains(filter.Search)
                || u.LastName.Contains(filter.Search));

        var total = await query.CountAsync();
        var items = await query
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(u => new
            {
                u.Id, u.Email, u.FirstName, u.LastName, u.PhoneNumber,
                CompanyId = u.CompanyId,
                Status = (int)u.Status, u.LastLoginAt, u.CreatedAt,
                Roles = u.UserRoles.Where(ur => !ur.IsDeleted).Select(ur => ur.Role.Name).ToList(),
                RoleIds = u.UserRoles.Where(ur => !ur.IsDeleted).Select(ur => ur.RoleId).ToList()
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
        var user = await _db.Users.AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted && u.CompanyId == tenantId);

        if (user == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "User not found"));

        return Ok(ApiResponse<object>.Ok(new
        {
            user.Id, user.Email, user.FirstName, user.LastName, user.PhoneNumber,
            Status = (int)user.Status, user.LastLoginAt, user.CreatedAt,
            Roles = user.UserRoles.Where(ur => !ur.IsDeleted).Select(ur => ur.Role.Name).ToList(),
            RoleIds = user.UserRoles.Where(ur => !ur.IsDeleted).Select(ur => ur.RoleId).ToList()
        }));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<object>>> Create([FromBody] CreateUserDto dto)
    {
        var tenantId = User.GetTenantId();

        // Check for duplicate email
        if (await _db.Users.AnyAsync(u => u.NormalizedEmail == dto.Email.ToUpperInvariant() && !u.IsDeleted))
            return BadRequest(ApiResponse.Fail("DUPLICATE_EMAIL", "A user with this email already exists"));

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = dto.Email,
            NormalizedEmail = dto.Email.ToUpperInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            PhoneNumber = dto.PhoneNumber,
            CompanyId = tenantId,
            SecurityStamp = Guid.NewGuid().ToString(),
            Status = EntityStatus.Active,
            EmailConfirmed = true
        };
        _db.Users.Add(user);

        if (dto.RoleIds?.Any() == true)
        {
            foreach (var roleId in dto.RoleIds)
            {
                _db.UserRoles.Add(new UserRole
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    RoleId = roleId,
                    TenantId = tenantId
                });
            }
        }

        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = user.Id }, ApiResponse<object>.Ok(new
        {
            user.Id, user.Email, user.FirstName, user.LastName
        }));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> Update(Guid id, [FromBody] UpdateUserDto dto)
    {
        var tenantId = User.GetTenantId();
        var user = await _db.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted && u.CompanyId == tenantId);

        if (user == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "User not found"));

        if (dto.FirstName != null) user.FirstName = dto.FirstName;
        if (dto.LastName != null) user.LastName = dto.LastName;
        if (dto.PhoneNumber != null) user.PhoneNumber = dto.PhoneNumber;
        if (dto.Language != null) user.Language = dto.Language;
        if (dto.Timezone != null) user.Timezone = dto.Timezone;
        if (dto.Currency != null) user.Currency = dto.Currency;
        if (dto.Status.HasValue) user.Status = (EntityStatus)dto.Status.Value;

        if (dto.RoleIds != null)
        {
            var existingRoles = user.UserRoles.Where(ur => !ur.IsDeleted).ToList();
            _db.UserRoles.RemoveRange(existingRoles);

            foreach (var roleId in dto.RoleIds)
            {
                _db.UserRoles.Add(new UserRole
                {
                    Id = Guid.NewGuid(),
                    UserId = id,
                    RoleId = roleId,
                    TenantId = tenantId
                });
            }
        }

        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "User updated"));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id)
    {
        var tenantId = User.GetTenantId();
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted && u.CompanyId == tenantId);

        if (user == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "User not found"));

        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "User deleted"));
    }
}
