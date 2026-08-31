using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Application.Interfaces;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

public class UserService : ICrudService<UserDto, CreateUserDto, UpdateUserDto, PagedRequest>
{
    private readonly ApplicationDbContext _db;

    public UserService(ApplicationDbContext db) => _db = db;

    public async Task<UserDto?> GetByIdAsync(Guid id)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);

        return user == null ? null : MapToDto(user);
    }
    // NOTE: User isolation should be enforced at the API controller level
    // via [Authorize] + tenant check, since a user may legitimately query
    // users from their own company for admin purposes.

    public async Task<PagedResult<UserDto>> GetListAsync(PagedRequest filter)
    {
        var query = _db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Where(u => !u.IsDeleted)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(u =>
                u.Email.Contains(filter.Search) ||
                u.FirstName.Contains(filter.Search) ||
                u.LastName.Contains(filter.Search));

        var totalCount = await query.CountAsync();

        query = filter.SortBy?.ToLower() switch
        {
            "email" => filter.SortDescending ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
            "firstname" => filter.SortDescending ? query.OrderByDescending(u => u.FirstName) : query.OrderBy(u => u.FirstName),
            _ => query.OrderBy(u => u.LastName)
        };

        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        return new PagedResult<UserDto>
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<UserDto> CreateAsync(CreateUserDto dto, string userId)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = dto.Email,
            NormalizedEmail = dto.Email.ToUpperInvariant(),
            PasswordHash = Infrastructure.Services.AuthService.HashPassword(dto.Password),
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            PhoneNumber = dto.PhoneNumber,
            CompanyId = dto.CompanyId,
            SecurityStamp = Guid.NewGuid().ToString(),
            Status = EntityStatus.Active
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
                    TenantId = dto.CompanyId
                });
            }
        }

        await _db.SaveChangesAsync();
        return MapToDto(user);
    }

    public async Task<UserDto?> UpdateAsync(Guid id, UpdateUserDto dto, string userId)
    {
        var user = await _db.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);

        if (user == null) return null;

        if (dto.FirstName != null) user.FirstName = dto.FirstName;
        if (dto.LastName != null) user.LastName = dto.LastName;
        if (dto.PhoneNumber != null) user.PhoneNumber = dto.PhoneNumber;
        if (dto.Language != null) user.Language = dto.Language;
        if (dto.Timezone != null) user.Timezone = dto.Timezone;
        if (dto.Currency != null) user.Currency = dto.Currency;
        if (dto.Status != null) user.Status = (EntityStatus)dto.Status.Value;

        if (dto.RoleIds != null)
        {
            _db.UserRoles.RemoveRange(user.UserRoles);
            foreach (var roleId in dto.RoleIds)
            {
                _db.UserRoles.Add(new UserRole
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    RoleId = roleId,
                    TenantId = user.CompanyId
                });
            }
        }

        await _db.SaveChangesAsync();
        return MapToDto(user);
    }

    public async Task<bool> SoftDeleteAsync(Guid id, string userId, string? reason = null)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null || user.IsDeleted) return false;

        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;
        user.DeletedBy = userId;
        user.DeletionReason = reason;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RestoreAsync(Guid id, string userId)
    {
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == id && u.IsDeleted);
        if (user == null) return false;

        user.IsDeleted = false;
        user.DeletedAt = null;
        user.DeletedBy = null;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<AuditEntryDto>> GetAuditHistoryAsync(Guid entityId)
    {
        return await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityType == EntityType.User && a.EntityId == entityId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new AuditEntryDto
            {
                Id = a.Id,
                Action = (int)a.Action,
                EntityType = (int)a.EntityType,
                EntityId = a.EntityId,
                EntityName = a.EntityName,
                OldValues = a.OldValues,
                NewValues = a.NewValues,
                UserId = a.UserId.ToString(),
                UserName = a.UserName,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();
    }

    private static UserDto MapToDto(User u) => new()
    {
        Id = u.Id,
        Email = u.Email,
        FirstName = u.FirstName,
        LastName = u.LastName,
        CompanyId = u.CompanyId,
        CompanyName = "", // Populated by caller when needed
        Roles = u.UserRoles?.Select(ur => ur.Role?.Name ?? "").ToList() ?? new List<string>()
    };
}
