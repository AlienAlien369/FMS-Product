using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Shared.Extensions;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Freebuff.Platform.Api.Controllers;

/// <summary>
/// Platform-level DeviceVendor catalog management — Super Admin only. The vendor
/// registry is a platform concern (its Code anchors the ingestion adapter lookup),
/// so tenants never touch it; they only see Active vendors in the Device form
/// dropdown via GET /api/v1/devices/vendors.
/// </summary>
[ApiController]
[Route("api/v1/admin/device-vendors")]
[Authorize(Roles = "SuperAdmin")]
public class AdminDeviceVendorsController : ControllerBase
{
    private readonly DeviceService _deviceService;

    public AdminDeviceVendorsController(DeviceService deviceService) => _deviceService = deviceService;

    // ── Full catalog (incl. inactive + device counts) ─────
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<DeviceVendorDto>>>> GetAll()
        => Ok(ApiResponse<List<DeviceVendorDto>>.Ok(await _deviceService.ListVendorsAdminAsync()));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<DeviceVendorDto>>> Create([FromBody] CreateDeviceVendorDto dto)
    {
        var result = await _deviceService.CreateVendorAsync(dto, User.GetUserIdString());
        return CreatedAtAction(nameof(GetAll), new { }, ApiResponse<DeviceVendorDto>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<DeviceVendorDto>>> Update(Guid id, [FromBody] UpdateDeviceVendorDto dto)
    {
        var result = await _deviceService.UpdateVendorAsync(id, dto, User.GetUserIdString());
        return Ok(ApiResponse<DeviceVendorDto>.Ok(result));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id)
    {
        await _deviceService.DeleteVendorAsync(id, User.GetUserIdString());
        return Ok(ApiResponse.Ok(message: "Vendor deleted"));
    }
}
