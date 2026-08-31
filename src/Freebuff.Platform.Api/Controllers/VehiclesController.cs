using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Shared.Extensions;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Freebuff.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class VehiclesController : ControllerBase
{
    private readonly VehicleService _vehicleService;

    public VehiclesController(VehicleService vehicleService) => _vehicleService = vehicleService;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<VehicleDto>>>> GetAll([FromQuery] PagedRequest filter)
    {
        var result = await _vehicleService.GetListAsync(filter);
        return Ok(ApiResponse<PagedResult<VehicleDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<VehicleDto>>> GetById(Guid id)
    {
        var result = await _vehicleService.GetByIdAsync(id);
        if (result == null) return NotFound(ApiResponse<VehicleDto>.Fail("NOT_FOUND", "Vehicle not found"));
        return Ok(ApiResponse<VehicleDto>.Ok(result));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<VehicleDto>>> Create([FromBody] CreateVehicleDto dto)
    {
        var userId = User.GetUserIdString();
        var result = await _vehicleService.CreateAsync(dto, userId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<VehicleDto>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<VehicleDto>>> Update(Guid id, [FromBody] UpdateVehicleDto dto)
    {
        var userId = User.GetUserIdString();
        var result = await _vehicleService.UpdateAsync(id, dto, userId);
        if (result == null) return NotFound(ApiResponse<VehicleDto>.Fail("NOT_FOUND", "Vehicle not found"));
        return Ok(ApiResponse<VehicleDto>.Ok(result));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id, [FromQuery] string? reason = null)
    {
        var userId = User.GetUserIdString();
        var deleted = await _vehicleService.SoftDeleteAsync(id, userId, reason);
        if (!deleted) return NotFound(ApiResponse.Fail("NOT_FOUND", "Vehicle not found"));
        return Ok(ApiResponse.Ok(message: "Vehicle deleted"));
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult<ApiResponse>> Restore(Guid id)
    {
        var userId = User.GetUserIdString();
        var restored = await _vehicleService.RestoreAsync(id, userId);
        if (!restored) return NotFound(ApiResponse.Fail("NOT_FOUND", "Vehicle not found or not deleted"));
        return Ok(ApiResponse.Ok(message: "Vehicle restored"));
    }

    [HttpGet("{id:guid}/audit")]
    public async Task<ActionResult<ApiResponse<List<AuditEntryDto>>>> GetAuditHistory(Guid id)
    {
        var result = await _vehicleService.GetAuditHistoryAsync(id);
        return Ok(ApiResponse<List<AuditEntryDto>>.Ok(result));
    }
}
