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

    /// <summary>
    /// Module catalog. A Module is the top-level grouping entity (dashboard,
    /// fleet operations, …). Pages inside a module come from the canonical
    /// PageRegistry, not from a "features" child table.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<object>>>> GetAll([FromQuery] PagedRequest filter)
    {
        var query = _db.Modules.AsNoTracking().Where(m => !m.IsDeleted);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(m => m.Name.Contains(filter.Search) || m.Code.Contains(filter.Search));

        query = query.OrderBy(m => m.DisplayOrder);

        var total = await query.CountAsync();
        var rows = await query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToListAsync();

        var items = rows.Select(m =>
        {
            var pages = PageRegistry.PagesInModule(m.Code).ToList();
            return (object)new
            {
                m.Id, m.Code, m.Name, m.Description, m.Icon,
                m.IsCore, Status = (int)m.Status, m.DisplayOrder,
                // A module contains pages, not features.
                PageCount = pages.Count(p => !p.Planned),
                PlannedPageCount = pages.Count(p => p.Planned),
                Pages = pages.Select(p => new { p.Key, p.Label, p.Planned, p.Nav, p.Route }).ToList()
            };
        }).ToList();

        return Ok(ApiResponse<PagedResult<object>>.Ok(new PagedResult<object>
        {
            Items = items,
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        }));
    }
}
