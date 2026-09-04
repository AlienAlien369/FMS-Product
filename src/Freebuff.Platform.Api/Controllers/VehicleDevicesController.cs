using Freebuff.Platform.Api.Authorization;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Shared.Extensions;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Freebuff.Platform.Api.Controllers;

/// <summary>
/// Vehicle ↔ Device assignments — a vehicle can host multiple devices at once
/// (primary tracker + dashcam + fuel sensor), each with a role. Gated on vehicle
/// permissions (device assignment is a vehicle-management action today).
/// </summary>
[ApiController]
[Route("api/v1/vehicles")]
[Authorize]
public class VehicleDevicesController : ControllerBase
{
    private readonly DeviceService _deviceService;

    public VehicleDevicesController(DeviceService deviceService) => _deviceService = deviceService;

    /// <summary>List a vehicle's currently assigned (active) devices with vendor + SIM detail.</summary>
    [HttpGet("{vehicleId:guid}/devices")]
    [RequirePermission("vehicle.view")]
    public async Task<ActionResult<ApiResponse<List<VehicleDeviceDto>>>> GetDevices(Guid vehicleId)
    {
        var result = await _deviceService.ListVehicleDevicesAsync(vehicleId);
        return Ok(ApiResponse<List<VehicleDeviceDto>>.Ok(result));
    }

    /// <summary>Assign an existing (active, same-company, unassigned) device to a vehicle with a role.</summary>
    [HttpPost("{vehicleId:guid}/devices")]
    [RequirePermission("vehicle.update")]
    public async Task<ActionResult<ApiResponse<VehicleDeviceDto>>> Assign(Guid vehicleId, [FromBody] AssignDeviceDto dto)
    {
        var result = await _deviceService.AssignDeviceAsync(vehicleId, dto, User.GetUserIdString());
        return Ok(ApiResponse<VehicleDeviceDto>.Ok(result));
    }

    /// <summary>Unassign a device from a vehicle (history preserved via AssignedTo).</summary>
    [HttpDelete("{vehicleId:guid}/devices/{assignmentId:guid}")]
    [RequirePermission("vehicle.update")]
    public async Task<ActionResult<ApiResponse>> Unassign(Guid vehicleId, Guid assignmentId, [FromQuery] string? reason = null)
    {
        await _deviceService.UnassignDeviceAsync(vehicleId, assignmentId, reason, User.GetUserIdString());
        return Ok(ApiResponse.Ok(message: "Device unassigned"));
    }
}
