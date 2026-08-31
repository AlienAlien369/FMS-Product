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
public class UsersController : ControllerBase
{
    private readonly IdentityDbContext _db;

    public UsersController(IdentityDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<UserDto>>>> GetAll([FromQuery] PagedRequest filter)
    {
        var query = _db.Users.AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Where(u => !u.IsDeleted).AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(u => u.Email.Contains(filter.Search) || u.FirstName.Contains(filter.Search) || u.LastName.Contains(filter.Search));

        var totalCount = await query.CountAsync();
        var items = await query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(u => new UserDto
            {
                Id = u.Id, Email = u.Email, FirstName = u.FirstName, LastName = u.LastName,
                CompanyId = u.CompanyId, Roles = u.UserRoles.Select(ur => ur.Role.Name).ToList()
            }).ToListAsync();

        return Ok(ApiResponse<PagedResult<UserDto>>.Ok(new PagedResult<UserDto>
        {
            Items = items, TotalCount = totalCount, Page = filter.Page, PageSize = filter.PageSize
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetById(Guid id)
    {
        var user = await _db.Users.AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null) return NotFound(ApiResponse<UserDto>.Fail("NOT_FOUND", "User not found"));
        return Ok(ApiResponse<UserDto>.Ok(new UserDto
        {
            Id = user.Id, Email = user.Email, FirstName = user.FirstName, LastName = user.LastName,
            CompanyId = user.CompanyId, Roles = user.UserRoles.Select(ur => ur.Role.Name).ToList()
        }));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<UserDto>>> Create([FromBody] CreateUserDto dto)
    {
        var user = new User
        {
            Id = Guid.NewGuid(), Email = dto.Email, NormalizedEmail = dto.Email.ToUpperInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            FirstName = dto.FirstName, LastName = dto.LastName, PhoneNumber = dto.PhoneNumber,
            CompanyId = dto.CompanyId, SecurityStamp = Guid.NewGuid().ToString(), Status = EntityStatus.Active
        };
        _db.Users.Add(user);

        if (dto.RoleIds?.Any() == true)
        {
            foreach (var roleId in dto.RoleIds)
                _db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = roleId, TenantId = dto.CompanyId });
        }
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = user.Id }, ApiResponse<UserDto>.Ok(new UserDto
        {
            Id = user.Id, Email = user.Email, FirstName = user.FirstName, LastName = user.LastName, CompanyId = user.CompanyId
        }));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null || user.IsDeleted) return NotFound(ApiResponse.Fail("NOT_FOUND", "User not found"));
        user.IsDeleted = true; user.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "User deleted"));
    }
}
