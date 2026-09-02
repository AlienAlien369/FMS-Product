using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class ModulesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public ModulesController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<object>>>> GetAll([FromQuery] PagedRequest filter)
    {
        var query = _db.Modules.AsNoTracking().Where(m => !m.IsDeleted);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(m => m.Name.Contains(filter.Search) || m.Code.Contains(filter.Search));

        query = query.OrderBy(m => m.DisplayOrder);

        var total = await query.CountAsync();
        var items = await query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(m => new
            {
                m.Id, m.Code, m.Name, m.Description, m.Icon, m.Route,
                m.IsCore, m.IsDeleted, m.ModuleVersion, m.Dependencies,
                Status = (int)m.Status, m.DisplayOrder,
                FeatureCount = m.Features.Count(f => !f.IsDeleted)
            }).ToListAsync();

        return Ok(ApiResponse<PagedResult<object>>.Ok(new PagedResult<object>
        {
            Items = items.Cast<object>().ToList(),
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        }));
    }

    [HttpGet("{id:guid}/features")]
    public async Task<ActionResult<ApiResponse<object>>> GetFeatures(Guid id)
    {
        var features = await _db.Features.AsNoTracking()
            .Where(f => f.ModuleId == id && !f.IsDeleted)
            .OrderBy(f => f.DisplayOrder)
            .Select(f => new { f.Id, f.Code, f.Name, f.Description, f.IsEnabledByDefault, Status = (int)f.Status, f.DisplayOrder })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(features));
    }
}
