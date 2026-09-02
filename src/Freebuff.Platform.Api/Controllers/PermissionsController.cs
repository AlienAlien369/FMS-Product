using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class PermissionsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public PermissionsController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<object>>>> GetAll([FromQuery] PagedRequest filter)
    {
        var query = _db.Permissions.AsNoTracking().Where(p => !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(p => p.Name.Contains(filter.Search) || p.Code.Contains(filter.Search) || p.Module.Contains(filter.Search));

        query = query.OrderBy(p => p.Module).ThenBy(p => p.DisplayOrder);

        var total = await query.CountAsync();
        var items = await query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(p => new
            {
                p.Id, p.Code, p.Name, p.Description, p.Module,
                Action = p.Action.ToString(),
                Status = (int)p.Status, p.DisplayOrder
            }).ToListAsync();

        return Ok(ApiResponse<PagedResult<object>>.Ok(new PagedResult<object>
        {
            Items = items.Cast<object>().ToList(),
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        }));
    }

    [HttpGet("grouped")]
    public async Task<ActionResult<ApiResponse<object>>> GetGrouped()
    {
        var permissions = await _db.Permissions.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.Module).ThenBy(p => p.DisplayOrder)
            .GroupBy(p => p.Module)
            .Select(g => new
            {
                Module = g.Key,
                Permissions = g.Select(p => new { p.Id, p.Code, p.Name, Action = p.Action.ToString() }).ToList()
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(permissions));
    }
}
