using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Api.Tenant.Data;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Tenant.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class ModulesController : ControllerBase
{
    private readonly TenantDbContext _db;
    public ModulesController(TenantDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<Module>>>> GetAll()
    {
        var modules = await _db.Modules.AsNoTracking().Where(m => !m.IsDeleted).OrderBy(m => m.DisplayOrder).ToListAsync();
        return Ok(ApiResponse<List<Module>>.Ok(modules));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<Module>>> GetById(Guid id)
    {
        var module = await _db.Modules.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted);
        if (module == null) return NotFound(ApiResponse<Module>.Fail("NOT_FOUND", "Module not found"));
        return Ok(ApiResponse<Module>.Ok(module));
    }
}
