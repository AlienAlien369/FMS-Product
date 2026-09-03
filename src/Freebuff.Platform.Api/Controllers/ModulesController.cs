using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
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
    /// fleet operations, …). Pages inside a module come from the DB (seeded from
    /// the canonical PageRegistry and manageable by SuperAdmin); the static
    /// registry is the fallback for modules that have no DB rows yet.
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
        var pagesByModule = (await _db.Pages.AsNoTracking().Where(p => !p.IsDeleted).ToListAsync())
            .GroupBy(p => p.ModuleId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.DisplayOrder).ToList());

        var items = rows.Select(m =>
        {
            var pages = pagesByModule.TryGetValue(m.Id, out var dbPages) && dbPages.Count > 0
                ? dbPages
                : PageRegistry.PagesInModule(m.Code).Select(ToPage).ToList();
            return (object)new
            {
                m.Id, m.Code, m.Name, m.Description, m.Icon,
                m.IsCore, Status = (int)m.Status, m.DisplayOrder,
                // A module contains pages, not features.
                PageCount = pages.Count(p => !p.Planned),
                PlannedPageCount = pages.Count(p => p.Planned),
                Pages = pages.Select(PageRegistry.PageView).ToList()
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

    private static Page ToPage(PageDefinition p) => new()
    {
        Id = Guid.Empty,
        Key = p.Key,
        Name = p.Label,
        Route = p.Route,
        Icon = p.Icon,
        Nav = p.Nav,
        AdminOnly = p.AdminOnly,
        Planned = p.Planned,
        IsCore = p.IsCore,
        Status = EntityStatus.Active,
        DisplayOrder = p.Order,
        Description = p.Description
    };
}
