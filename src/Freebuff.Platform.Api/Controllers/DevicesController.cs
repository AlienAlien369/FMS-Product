using Freebuff.Platform.Api.Authorization;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Shared.Extensions;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Freebuff.Platform.Api.Controllers;

/// <summary>
/// Device + SIM management. Gated on the registered "device" page permissions
/// (device.view/create/update/delete) — the same 6-action model as every page.
/// </summary>
[ApiController]
[Route("api/v1/devices")]
[Authorize]
public class DevicesController : ControllerBase
{
    private readonly DeviceService _deviceService;

    public DevicesController(DeviceService deviceService) => _deviceService = deviceService;

    [HttpPost]
    [RequirePermission("device.create")]
    public async Task<ActionResult<ApiResponse<DeviceDto>>> Register([FromBody] CreateDeviceDto dto)
    {
        var result = await _deviceService.RegisterDeviceAsync(dto, User.GetUserIdString());
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<DeviceDto>.Ok(result));
    }

    [HttpGet]
    [RequirePermission("device.view")]
    public async Task<ActionResult<ApiResponse<Freebuff.Platform.Shared.Models.PagedResult<DeviceDto>>>> GetAll(
        [FromQuery] Freebuff.Platform.Shared.Models.PagedRequest filter, [FromQuery] int? status = null, [FromQuery] Guid? vendorId = null)
    {
        var result = await _deviceService.ListAsync(filter, status, vendorId);
        return Ok(ApiResponse<Freebuff.Platform.Shared.Models.PagedResult<DeviceDto>>.Ok(result));
    }

    [HttpGet("vendors")]
    [RequirePermission("device.view")]
    public async Task<ActionResult<ApiResponse<List<DeviceVendorDto>>>> GetVendors()
    {
        var result = await _deviceService.ListVendorsAsync();
        return Ok(ApiResponse<List<DeviceVendorDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission("device.view")]
    public async Task<ActionResult<ApiResponse<DeviceDto>>> GetById(Guid id)
    {
        // Full detail (incl. SIMs + current vehicle) for assignment flows.
        var result = await _deviceService.GetDetailAsync(id);
        if (result == null) return NotFound(ApiResponse<DeviceDto>.Fail("NOT_FOUND", "Device not found"));
        return Ok(ApiResponse<DeviceDto>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("device.update")]
    public async Task<ActionResult<ApiResponse<DeviceDto>>> Update(Guid id, [FromBody] DeviceUpdateDto dto)
    {
        var result = await _deviceService.UpdateAsync(id, dto, User.GetUserIdString());
        return Ok(ApiResponse<DeviceDto>.Ok(result));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("device.delete")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id, [FromQuery] string? reason = null)
    {
        await _deviceService.DeleteAsync(id, reason, User.GetUserIdString());
        return Ok(ApiResponse.Ok(message: "Device deleted"));
    }

    [HttpPost("{id:guid}/sims")]
    [RequirePermission("device.update")]
    public async Task<ActionResult<ApiResponse<DeviceSimDto>>> AddSim(Guid id, [FromBody] CreateDeviceSimDto dto)
    {
        var result = await _deviceService.AddSimAsync(id, dto, User.GetUserIdString());
        return Ok(ApiResponse<DeviceSimDto>.Ok(result));
    }
}
