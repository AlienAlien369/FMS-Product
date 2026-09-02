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
public class LanguagesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public LanguagesController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<object>>>> GetAll([FromQuery] PagedRequest filter)
    {
        var query = _db.Languages.AsNoTracking().Where(l => !l.IsDeleted);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(l => l.Name.Contains(filter.Search) || l.Code.Contains(filter.Search));

        query = query.OrderBy(l => l.DisplayOrder);

        var total = await query.CountAsync();
        var items = await query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(l => new
            {
                l.Id, l.Code, l.Name, l.NativeName, l.IsRightToLeft, l.IsDefault,
                Status = (int)l.Status, l.DisplayOrder
            }).ToListAsync();

        return Ok(ApiResponse<PagedResult<object>>.Ok(new PagedResult<object>
        {
            Items = items.Cast<object>().ToList(),
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        }));
    }
}
