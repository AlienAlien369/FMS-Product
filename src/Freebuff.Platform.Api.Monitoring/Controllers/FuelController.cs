using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Api.Monitoring.Data;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Monitoring.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class FuelController : ControllerBase
{
    private readonly MonitoringDbContext _db;
    public FuelController(MonitoringDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<FuelRecord>>>> GetAll([FromQuery] PagedRequest filter, [FromQuery] Guid? vehicleId)
    {
        var tenantId = User.FindFirst("tenant_id")?.Value;
        var query = _db.FuelRecords.AsNoTracking().Where(f => !f.IsDeleted).AsQueryable();
        if (Guid.TryParse(tenantId, out var tid)) query = query.Where(f => f.CompanyId == tid);
        if (vehicleId.HasValue) query = query.Where(f => f.VehicleId == vehicleId.Value);

        var totalCount = await query.CountAsync();
        var items = await query.OrderByDescending(f => f.RecordDate)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToListAsync();

        return Ok(ApiResponse<PagedResult<FuelRecord>>.Ok(new PagedResult<FuelRecord>
        {
            Items = items, TotalCount = totalCount, Page = filter.Page, PageSize = filter.PageSize
        }));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<FuelRecord>>> Create([FromBody] CreateFuelRecordDto dto)
    {
        var tenantId = User.FindFirst("tenant_id")?.Value ?? throw new UnauthorizedAccessException("No tenant");
        var record = new FuelRecord
        {
            Id = Guid.NewGuid(),
            VehicleId = dto.VehicleId,
            CompanyId = Guid.Parse(tenantId),
            FuelType = dto.FuelType,
            Quantity = dto.Quantity,
            Unit = dto.Unit,
            PricePerUnit = dto.PricePerUnit,
            TotalCost = dto.TotalCost,
            OdometerReading = dto.OdometerReading,
            FuelLevel = dto.FuelLevel,
            IsRefueling = dto.IsRefueling,
            Notes = dto.Notes,
            RecordDate = dto.RecordDate
        };
        _db.FuelRecords.Add(record);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), ApiResponse<FuelRecord>.Ok(record));
    }
}
