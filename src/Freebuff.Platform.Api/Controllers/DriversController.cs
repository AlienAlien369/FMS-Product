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
public class DriversController : ControllerBase
{
    private readonly DriverService _driverService;

    public DriversController(DriverService driverService) => _driverService = driverService;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<DriverDto>>>> GetAll([FromQuery] PagedRequest filter)
    {
        var result = await _driverService.GetListAsync(filter);
        return Ok(ApiResponse<PagedResult<DriverDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<DriverDto>>> GetById(Guid id)
    {
        var result = await _driverService.GetByIdAsync(id);
        if (result == null) return NotFound(ApiResponse<DriverDto>.Fail("NOT_FOUND", "Driver not found"));
        return Ok(ApiResponse<DriverDto>.Ok(result));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<DriverDto>>> Create([FromBody] CreateDriverDto dto)
    {
        var userId = User.GetUserIdString();
        var result = await _driverService.CreateAsync(dto, userId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<DriverDto>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<DriverDto>>> Update(Guid id, [FromBody] UpdateDriverDto dto)
    {
        var userId = User.GetUserIdString();
        var result = await _driverService.UpdateAsync(id, dto, userId);
        if (result == null) return NotFound(ApiResponse<DriverDto>.Fail("NOT_FOUND", "Driver not found"));
        return Ok(ApiResponse<DriverDto>.Ok(result));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id, [FromQuery] string? reason = null)
    {
        var userId = User.GetUserIdString();
        var deleted = await _driverService.SoftDeleteAsync(id, userId, reason);
        if (!deleted) return NotFound(ApiResponse.Fail("NOT_FOUND", "Driver not found"));
        return Ok(ApiResponse.Ok(message: "Driver deleted"));
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult<ApiResponse>> Restore(Guid id)
    {
        var userId = User.GetUserIdString();
        var restored = await _driverService.RestoreAsync(id, userId);
        if (!restored) return NotFound(ApiResponse.Fail("NOT_FOUND", "Driver not found or not deleted"));
        return Ok(ApiResponse.Ok(message: "Driver restored"));
    }
}
