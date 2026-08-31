using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Api.Fleet.Data;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Fleet.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class DriversController : ControllerBase
{
    private readonly FleetDbContext _db;
    public DriversController(FleetDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<DriverDto>>>> GetAll([FromQuery] PagedRequest filter)
    {
        var tenantId = User.FindFirst("tenant_id")?.Value;
        var query = _db.Drivers.AsNoTracking().Where(d => !d.IsDeleted).AsQueryable();
        if (Guid.TryParse(tenantId, out var tid)) query = query.Where(d => d.CompanyId == tid);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(d => d.FirstName.Contains(filter.Search) || d.LastName.Contains(filter.Search));

        var totalCount = await query.CountAsync();
        var items = await query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(d => new DriverDto
            {
                Id = d.Id, EmployeeId = d.EmployeeId, FirstName = d.FirstName, LastName = d.LastName,
                FullName = d.FirstName + " " + d.LastName, PhoneNumber = d.PhoneNumber, Email = d.Email,
                LicenseNumber = d.LicenseNumber, LicenseExpiry = d.LicenseExpiry, CompanyId = d.CompanyId,
                Status = (int)d.Status, SafetyScore = d.SafetyScore, BehaviourScore = d.BehaviourScore
            }).ToListAsync();

        return Ok(ApiResponse<PagedResult<DriverDto>>.Ok(new PagedResult<DriverDto>
        {
            Items = items, TotalCount = totalCount, Page = filter.Page, PageSize = filter.PageSize
        }));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<DriverDto>>> Create([FromBody] CreateDriverDto dto)
    {
        var tenantId = User.FindFirst("tenant_id")?.Value ?? throw new UnauthorizedAccessException("No tenant");
        var driver = new Driver
        {
            Id = Guid.NewGuid(), EmployeeId = dto.EmployeeId, FirstName = dto.FirstName, LastName = dto.LastName,
            PhoneNumber = dto.PhoneNumber, Email = dto.Email, LicenseNumber = dto.LicenseNumber,
            LicenseExpiry = dto.LicenseExpiry, Address = dto.Address, City = dto.City, Country = dto.Country,
            CompanyId = Guid.Parse(tenantId), Status = DriverStatus.Active
        };
        _db.Drivers.Add(driver);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), ApiResponse<DriverDto>.Ok(new DriverDto
        {
            Id = driver.Id, EmployeeId = driver.EmployeeId, FirstName = driver.FirstName, LastName = driver.LastName,
            FullName = driver.FullName, Status = (int)driver.Status
        }));
    }
}
