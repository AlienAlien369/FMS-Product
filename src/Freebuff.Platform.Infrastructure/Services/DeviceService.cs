using System.Text.RegularExpressions;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>
/// Device + SIM + vehicle-assignment management. Every operation is tenant-scoped:
/// a company user only ever touches devices/vehicles of their own company; the
/// company id in a request is NEVER trusted for non-SuperAdmins.
/// </summary>
public class DeviceService
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public DeviceService(ApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public Guid ResolveCompanyId(Guid? requested)
    {
        if (_tenant.IsSuperAdmin)
        {
            if (requested == null) throw new ArgumentException("CompanyId is required for SuperAdmin device registration");
            return requested.Value;
        }
        return _tenant.TenantId ?? throw new UnauthorizedAccessException("No tenant context");
    }

    public async Task<DeviceDto> RegisterDeviceAsync(CreateDeviceDto dto, string? userId)
    {
        var companyId = ResolveCompanyId(dto.CompanyId);
        if (string.IsNullOrWhiteSpace(dto.IdentityValue))
            throw new ArgumentException("Device identity (IMEI/serial) is required");
        if (dto.IdentityValue.Length > 100) throw new ArgumentException("Device identity is too long");

        var vendor = await _db.DeviceVendors.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Code == dto.VendorCode && !v.IsDeleted)
            ?? throw new KeyNotFoundException($"Vendor '{dto.VendorCode}' not found");
        if (vendor.Status != DeviceStatus.Active)
            throw new InvalidOperationException($"Vendor '{dto.VendorCode}' is not active");

        var identityType = (DeviceIdentityType)dto.IdentityType;
        var duplicate = await _db.Devices.AsNoTracking()
            .AnyAsync(d => d.CompanyId == companyId && d.IdentityType == identityType
                        && d.IdentityValue == dto.IdentityValue && !d.IsDeleted);
        if (duplicate)
            throw new InvalidOperationException($"A device with this {identityType} already exists in the company");

        var now = DateTime.UtcNow;
        var device = new Device
        {
            Id = Guid.NewGuid(),
            TenantId = companyId,
            CompanyId = companyId,
            VendorId = vendor.Id,
            DeviceType = (DeviceType)dto.DeviceType,
            IdentityType = identityType,
            IdentityValue = dto.IdentityValue.Trim(),
            Model = dto.Model,
            FirmwareVersion = dto.FirmwareVersion,
            Status = DeviceStatus.Active,
            ActivatedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = userId,
            UpdatedBy = userId
        };

        if (dto.Sims != null)
        {
            var primary = dto.Sims.FirstOrDefault(s => s.IsPrimary);
            if (dto.Sims.Count(s => s.IsPrimary) > 1)
                throw new ArgumentException("Only one SIM can be primary per device");
            foreach (var simDto in dto.Sims)
            {
                _db.DeviceSims.Add(new DeviceSim
                {
                    Id = Guid.NewGuid(),
                    TenantId = companyId,
                    DeviceId = device.Id,
                    Iccid = simDto.Iccid,
                    PhoneNumber = simDto.PhoneNumber,
                    Carrier = simDto.Carrier,
                    Status = (DeviceSimStatus)simDto.Status,
                    IsPrimary = simDto.IsPrimary,
                    ActivatedAt = simDto.Status == (int)DeviceSimStatus.Active ? now : null,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }

        _db.Devices.Add(device);
        await _db.SaveChangesAsync();

        var vendorNames = await GetVendorMapAsync();
        var created = await LoadDeviceDtoAsync(device.Id, vendorNames) ?? throw new InvalidOperationException("Device registration failed");
        return created;
    }

    public async Task<Freebuff.Platform.Shared.Models.PagedResult<DeviceDto>> ListAsync(
        Freebuff.Platform.Shared.Models.PagedRequest filter, int? status = null, Guid? vendorId = null)
    {
        var companyId = _tenant.IsSuperAdmin ? (Guid?)null : _tenant.TenantId;
        var query = _db.Devices.AsNoTracking().Where(d => !d.IsDeleted);
        if (companyId != null) query = query.Where(d => d.CompanyId == companyId.Value);
        if (status != null) query = query.Where(d => (int)d.Status == status.Value);
        if (vendorId != null) query = query.Where(d => d.VendorId == vendorId.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            query = query.Where(d => d.IdentityValue.ToLower().Contains(search)
                || (d.Model != null && d.Model.ToLower().Contains(search)));
        }

        var totalCount = await query.CountAsync();
        var sortDesc = filter.SortDescending;
        query = (filter.SortBy ?? "createdAt").ToLowerInvariant() switch
        {
            "identityvalue" => sortDesc ? query.OrderByDescending(d => d.IdentityValue) : query.OrderBy(d => d.IdentityValue),
            "devicetype" => sortDesc ? query.OrderByDescending(d => d.DeviceType) : query.OrderBy(d => d.DeviceType),
            "status" => sortDesc ? query.OrderByDescending(d => d.Status) : query.OrderBy(d => d.Status),
            "lastseenat" => sortDesc ? query.OrderByDescending(d => d.LastSeenAt) : query.OrderBy(d => d.LastSeenAt),
            _ => sortDesc ? query.OrderByDescending(d => d.CreatedAt) : query.OrderBy(d => d.CreatedAt)
        };

        var page = Math.Max(filter.Page, 1);
        var pageSize = filter.PageSize <= 0 || filter.PageSize > 100 ? 20 : filter.PageSize;
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var ids = items.Select(d => d.Id).ToList();

        var vendors = await GetVendorMapAsync();
        var sims = await _db.DeviceSims.AsNoTracking()
            .Where(s => ids.Contains(s.DeviceId) && !s.IsDeleted)
            .GroupBy(s => s.DeviceId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(ToSimDto).ToList());

        // Resolve each device's currently-active vehicle assignment (multi-device aware:
        // a device can only be actively assigned to one vehicle at a time).
        var assignments = await _db.VehicleDevices.AsNoTracking()
            .Where(vd => ids.Contains(vd.DeviceId) && vd.AssignedTo == null && !vd.IsDeleted)
            .ToListAsync();
        var assignmentVehicleIds = assignments.Select(a => a.VehicleId).Distinct().ToList();
        var assignmentRegs = await _db.Vehicles.AsNoTracking()
            .Where(v => assignmentVehicleIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.RegistrationNumber);

        var dtos = items.Select(device =>
        {
            vendors.TryGetValue(device.VendorId ?? Guid.Empty, out var vendor);
            var assignment = assignments.FirstOrDefault(a => a.DeviceId == device.Id);
            return new DeviceDto
            {
                Id = device.Id,
                CompanyId = device.CompanyId,
                VendorId = device.VendorId,
                VendorCode = vendor?.Code,
                VendorName = vendor?.Name,
                DeviceType = (int)device.DeviceType,
                DeviceTypeOverride = device.DeviceTypeOverride,
                IdentityType = (int)device.IdentityType,
                IdentityValue = device.IdentityValue,
                Model = device.Model,
                FirmwareVersion = device.FirmwareVersion,
                Status = (int)device.Status,
                InstallDate = device.InstallDate,
                ActivatedAt = device.ActivatedAt,
                LastSeenAt = device.LastSeenAt,
                CreatedAt = device.CreatedAt,
                Sims = sims.TryGetValue(device.Id, out var s) ? s : new List<DeviceSimDto>(),
                CurrentVehicleId = assignment?.VehicleId,
                CurrentVehicleRegistration = assignment != null && assignmentRegs.TryGetValue(assignment.VehicleId, out var reg) ? reg : null
            };
        }).ToList();

        return new Freebuff.Platform.Shared.Models.PagedResult<DeviceDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<DeviceDto> UpdateAsync(Guid deviceId, DeviceUpdateDto dto, string? userId)
    {
        var companyId = _tenant.IsSuperAdmin ? (Guid?)null : _tenant.TenantId;
        var query = _db.Devices.Where(d => d.Id == deviceId && !d.IsDeleted);
        if (companyId != null) query = query.Where(d => d.CompanyId == companyId.Value);
        var device = await query.FirstOrDefaultAsync() ?? throw new KeyNotFoundException("Device not found");

        if (dto.Model != null) device.Model = dto.Model;
        if (dto.FirmwareVersion != null) device.FirmwareVersion = dto.FirmwareVersion;
        if (dto.DeviceType != null) device.DeviceType = (DeviceType)dto.DeviceType.Value;
        if (dto.Status != null) device.Status = (DeviceStatus)dto.Status.Value;
        if (dto.RawMetadata != null) device.RawMetadata = dto.RawMetadata;
        device.UpdatedAt = DateTime.UtcNow;
        device.UpdatedBy = userId;
        await _db.SaveChangesAsync();

        var vendors = await GetVendorMapAsync();
        return await LoadDeviceDtoAsync(deviceId, vendors) ?? throw new InvalidOperationException("Device update failed");
    }

    /// <summary>Soft-deletes a device and ends its active assignment (history preserved).</summary>
    public async Task DeleteAsync(Guid deviceId, string? reason, string? userId)
    {
        var companyId = _tenant.IsSuperAdmin ? (Guid?)null : _tenant.TenantId;
        var query = _db.Devices.Where(d => d.Id == deviceId && !d.IsDeleted);
        if (companyId != null) query = query.Where(d => d.CompanyId == companyId.Value);
        var device = await query.FirstOrDefaultAsync() ?? throw new KeyNotFoundException("Device not found");

        var now = DateTime.UtcNow;
        device.IsDeleted = true;
        device.DeletedAt = now;
        device.DeletedBy = userId;
        device.DeletionReason = reason;
        device.UpdatedAt = now;

        var active = await _db.VehicleDevices.Where(vd => vd.DeviceId == deviceId && vd.AssignedTo == null && !vd.IsDeleted).ToListAsync();
        foreach (var assignment in active)
        {
            assignment.AssignedTo = now;
            assignment.UnassignReason = reason ?? "device deleted";
            assignment.UpdatedAt = now;
        }
        await _db.SaveChangesAsync();
    }

    public async Task<List<DeviceVendorDto>> ListVendorsAsync(bool activeOnly = true)
    {
        var query = _db.DeviceVendors.AsNoTracking().Where(v => !v.IsDeleted);
        if (activeOnly) query = query.Where(v => v.Status == DeviceStatus.Active);
        return await query
            .OrderBy(v => v.Name)
            .Select(v => new DeviceVendorDto
            {
                Id = v.Id,
                Code = v.Code,
                Name = v.Name,
                Description = v.Description,
                AdapterVersion = v.AdapterVersion,
                ProtocolType = (int)v.ProtocolType,
                PayloadFormat = v.PayloadFormat,
                ListenerConfig = v.ListenerConfig,
                Capabilities = v.Capabilities,
                Status = (int)v.Status
            })
            .ToListAsync();
    }

    /// <summary>Full vendor catalog for the SuperAdmin Device Vendors page — includes
    /// inactive rows and the number of devices registered under each vendor.</summary>
    public async Task<List<DeviceVendorDto>> ListVendorsAdminAsync()
    {
        var vendors = await _db.DeviceVendors.AsNoTracking()
            .Where(v => !v.IsDeleted)
            .OrderBy(v => v.Name)
            .ToListAsync();
        var counts = await _db.Devices.AsNoTracking()
            .Where(d => !d.IsDeleted && d.VendorId != null)
            .GroupBy(d => d.VendorId!.Value)
            .ToDictionaryAsync(g => g.Key, g => g.Count());

        return vendors.Select(v => ToVendorDto(v, counts.TryGetValue(v.Id, out var c) ? c : 0)).ToList();
    }

    public async Task<DeviceVendorDto> CreateVendorAsync(CreateDeviceVendorDto dto, string? userId)
    {
        var code = (dto.Code ?? string.Empty).Trim().ToLowerInvariant();
        if (!Regex.IsMatch(code, "^[a-z0-9]+(-[a-z0-9]+)*$"))
            throw new ArgumentException("Vendor code must be lowercase kebab-case (letters, digits and dashes only)");
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("Vendor name is required");

        var exists = await _db.DeviceVendors.AnyAsync(v => v.Code == code && !v.IsDeleted);
        if (exists) throw new InvalidOperationException($"A vendor with code '{code}' already exists");

        var now = DateTime.UtcNow;
        var vendor = new DeviceVendor
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = dto.Name.Trim(),
            Description = dto.Description,
            AdapterVersion = string.IsNullOrWhiteSpace(dto.AdapterVersion) ? "1.0.0" : dto.AdapterVersion.Trim(),
            ProtocolType = (DeviceProtocolType)dto.ProtocolType,
            PayloadFormat = dto.PayloadFormat,
            Status = (DeviceStatus)dto.Status,
            ListenerConfig = dto.ListenerConfig,
            Capabilities = dto.Capabilities,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = userId,
            UpdatedBy = userId
        };
        _db.DeviceVendors.Add(vendor);
        await _db.SaveChangesAsync();
        return ToVendorDto(vendor, 0);
    }

    /// <summary>Updates catalog metadata + activation. The vendor Code is immutable
    /// (it anchors adapter lookup and existing Device rows), so no rename path exists.</summary>
    public async Task<DeviceVendorDto> UpdateVendorAsync(Guid id, UpdateDeviceVendorDto dto, string? userId)
    {
        var vendor = await _db.DeviceVendors.FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted)
            ?? throw new KeyNotFoundException("Vendor not found");

        if (dto.Name != null)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Vendor name cannot be empty");
            vendor.Name = dto.Name.Trim();
        }
        if (dto.Description != null) vendor.Description = dto.Description;
        if (dto.AdapterVersion != null) vendor.AdapterVersion = dto.AdapterVersion;
        if (dto.ProtocolType != null) vendor.ProtocolType = (DeviceProtocolType)dto.ProtocolType.Value;
        if (dto.PayloadFormat != null) vendor.PayloadFormat = dto.PayloadFormat;
        if (dto.Status != null) vendor.Status = (DeviceStatus)dto.Status.Value;
        if (dto.ListenerConfig != null) vendor.ListenerConfig = dto.ListenerConfig;
        if (dto.Capabilities != null) vendor.Capabilities = dto.Capabilities;
        vendor.UpdatedAt = DateTime.UtcNow;
        vendor.UpdatedBy = userId;
        await _db.SaveChangesAsync();

        var count = await _db.Devices.CountAsync(d => d.VendorId == id && !d.IsDeleted);
        return ToVendorDto(vendor, count);
    }

    /// <summary>
    /// Deletes a vendor row (soft). Vendors anchored to a registered ingestion adapter
    /// cannot be deleted — their code routes raw payloads — and vendors that still have
    /// registered devices are blocked so devices never dangle on a deleted vendor.
    /// </summary>
    public async Task DeleteVendorAsync(Guid id, string? userId)
    {
        var vendor = await _db.DeviceVendors.FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted)
            ?? throw new KeyNotFoundException("Vendor not found");

        if (AdapterBackedVendorCodes.Contains(vendor.Code))
            throw new InvalidOperationException(
                $"Vendor '{vendor.Name}' ships with a registered ingestion adapter and cannot be deleted — deactivate it instead.");

        var deviceCount = await _db.Devices.CountAsync(d => d.VendorId == id && !d.IsDeleted);
        if (deviceCount > 0)
            throw new InvalidOperationException(
                $"Vendor '{vendor.Name}' has {deviceCount} registered device(s). Remove those devices first.");

        var now = DateTime.UtcNow;
        vendor.IsDeleted = true;
        vendor.DeletedAt = now;
        vendor.DeletedBy = userId;
        vendor.UpdatedAt = now;
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Vendor codes backed by a registered IVendorAdapter in Freebuff.Platform.Ingestion.
    /// Single source of truth: derived from the adapter attributes, so adding an adapter
    /// for a new vendor automatically protects its catalog row.
    /// </summary>
    private static readonly HashSet<string> AdapterBackedVendorCodes = new(
        Freebuff.Platform.Ingestion.Registry.VendorAdapterRegistry.CreateBuiltIn().All.Select(a => a.VendorCode),
        StringComparer.OrdinalIgnoreCase);

    private static DeviceVendorDto ToVendorDto(DeviceVendor v, int deviceCount) => new()
    {
        Id = v.Id,
        Code = v.Code,
        Name = v.Name,
        Description = v.Description,
        AdapterVersion = v.AdapterVersion,
        ProtocolType = (int)v.ProtocolType,
        PayloadFormat = v.PayloadFormat,
        ListenerConfig = v.ListenerConfig,
        Capabilities = v.Capabilities,
        Status = (int)v.Status,
        DeviceCount = deviceCount
    };

    public async Task<DeviceDto?> GetDetailAsync(Guid deviceId)
    {
        Guid? tenantId = _tenant.IsSuperAdmin ? null : _tenant.TenantId;
        var query = _db.Devices.AsNoTracking().Where(d => d.Id == deviceId && !d.IsDeleted);
        if (tenantId != null) query = query.Where(d => d.CompanyId == tenantId.Value);
        if (!await query.AnyAsync()) return null;
        var vendors = await GetVendorMapAsync();
        return await LoadDeviceDtoAsync(deviceId, vendors);
    }

    public async Task<DeviceSimDto> AddSimAsync(Guid deviceId, CreateDeviceSimDto dto, string? userId)
    {
        // Scope to the device's own company (never a client-supplied company id).
        var isSa = _tenant.IsSuperAdmin;
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId && !d.IsDeleted
                && (isSa || d.CompanyId == _tenant.TenantId))
            ?? throw new KeyNotFoundException("Device not found");
        var companyId = device.CompanyId;

        if (dto.IsPrimary)
        {
            var hasPrimary = await _db.DeviceSims.AnyAsync(s => s.DeviceId == deviceId && s.IsPrimary && !s.IsDeleted);
            if (hasPrimary) throw new InvalidOperationException("Device already has a primary SIM");
        }

        var now = DateTime.UtcNow;
        var sim = new DeviceSim
        {
            Id = Guid.NewGuid(),
            TenantId = companyId,
            DeviceId = deviceId,
            Iccid = dto.Iccid,
            PhoneNumber = dto.PhoneNumber,
            Carrier = dto.Carrier,
            Status = (DeviceSimStatus)dto.Status,
            IsPrimary = dto.IsPrimary,
            ActivatedAt = dto.Status == (int)DeviceSimStatus.Active ? now : null,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = userId
        };
        _db.DeviceSims.Add(sim);
        await _db.SaveChangesAsync();
        return ToSimDto(sim);
    }

    public async Task<List<VehicleDeviceDto>> ListVehicleDevicesAsync(Guid vehicleId)
    {
        var companyId = _tenant.IsSuperAdmin ? (Guid?)null : _tenant.TenantId;
        var query = _db.VehicleDevices.AsNoTracking()
            .Where(vd => vd.VehicleId == vehicleId && vd.AssignedTo == null);

        if (companyId != null)
        {
            var tenantId = companyId.Value;
            query = query.Where(vd => _db.Devices.Any(d => d.Id == vd.DeviceId && d.CompanyId == tenantId && !d.IsDeleted));
        }

        var assignments = await query.ToListAsync();
        var deviceIds = assignments.Select(a => a.DeviceId).ToList();

        var devices = await _db.Devices.AsNoTracking()
            .Where(d => deviceIds.Contains(d.Id) && !d.IsDeleted)
            .ToDictionaryAsync(d => d.Id);
        var vendors = await GetVendorMapAsync();
        var sims = await _db.DeviceSims.AsNoTracking()
            .Where(s => deviceIds.Contains(s.DeviceId) && !s.IsDeleted)
            .GroupBy(s => s.DeviceId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(ToSimDto).ToList());

        return assignments
            .Where(a => devices.TryGetValue(a.DeviceId, out var d) && d != null)
            .Select(a =>
            {
                var device = devices[a.DeviceId];
                vendors.TryGetValue(device.VendorId ?? Guid.Empty, out var vendor);
                return new VehicleDeviceDto
                {
                    Id = a.Id,
                    VehicleId = a.VehicleId,
                    DeviceId = a.DeviceId,
                    Role = (int)a.Role,
                    RoleName = a.Role.ToString(),
                    AssignedFrom = a.AssignedFrom,
                    AssignedTo = a.AssignedTo,
                    UnassignReason = a.UnassignReason,
                    VendorCode = vendor?.Code,
                    VendorName = vendor?.Name,
                    DeviceType = (int)device.DeviceType,
                    DeviceTypeOverride = device.DeviceTypeOverride,
                    IdentityType = (int)device.IdentityType,
                    IdentityValue = device.IdentityValue,
                    Model = device.Model,
                    DeviceStatus = (int)device.Status,
                    Sims = sims.TryGetValue(a.DeviceId, out var s) ? s : new List<DeviceSimDto>()
                };
            })
            .ToList();
    }

    public async Task<VehicleDeviceDto> AssignDeviceAsync(Guid vehicleId, AssignDeviceDto dto, string? userId)
    {
        var role = (VehicleDeviceRole)dto.Role;
        var isSa = _tenant.IsSuperAdmin;

        var vehicle = await _db.Vehicles.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == vehicleId && !v.IsDeleted
                && (isSa || v.CompanyId == _tenant.TenantId))
            ?? throw new KeyNotFoundException("Vehicle not found");

        // The device must belong to the SAME company as the vehicle — the client
        // never decides the tenant.
        var device = await _db.Devices.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == dto.DeviceId && d.CompanyId == vehicle.CompanyId && !d.IsDeleted)
            ?? throw new KeyNotFoundException("Device not found");
        var companyId = vehicle.CompanyId;
        if (device.Status != DeviceStatus.Active)
            throw new InvalidOperationException("Only active devices can be assigned");

        var existing = await _db.VehicleDevices.AsNoTracking()
            .FirstOrDefaultAsync(vd => vd.DeviceId == dto.DeviceId && vd.AssignedTo == null && !vd.IsDeleted);
        if (existing != null)
        {
            var assignedVehicle = await _db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == existing.VehicleId);
            throw new InvalidOperationException($"Device is already assigned to {(assignedVehicle?.RegistrationNumber ?? "another vehicle")}");
        }

        var sameRole = await _db.VehicleDevices.AsNoTracking()
            .AnyAsync(vd => vd.VehicleId == vehicleId && vd.Role == role && vd.AssignedTo == null && !vd.IsDeleted);
        if (sameRole)
            throw new InvalidOperationException($"Vehicle already has an active device with role '{role}'");

        var now = DateTime.UtcNow;
        var assignment = new VehicleDevice
        {
            Id = Guid.NewGuid(),
            TenantId = companyId,
            VehicleId = vehicleId,
            DeviceId = dto.DeviceId,
            Role = role,
            AssignedFrom = now,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = userId
        };
        _db.VehicleDevices.Add(assignment);
        await _db.SaveChangesAsync();

        var list = await ListVehicleDevicesAsync(vehicleId);
        return list.First(vd => vd.Id == assignment.Id);
    }

    public async Task UnassignDeviceAsync(Guid vehicleId, Guid assignmentId, string? reason, string? userId)
    {
        var isSa = _tenant.IsSuperAdmin;
        var assignment = await _db.VehicleDevices
            .FirstOrDefaultAsync(vd => vd.Id == assignmentId && vd.VehicleId == vehicleId && vd.AssignedTo == null && !vd.IsDeleted)
            ?? throw new KeyNotFoundException("Active assignment not found");

        var device = await _db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == assignment.DeviceId);
        if (device == null || (!isSa && device.CompanyId != _tenant.TenantId))
            throw new KeyNotFoundException("Assignment not found");

        var now = DateTime.UtcNow;
        assignment.AssignedTo = now;
        assignment.UnassignReason = reason;
        assignment.UpdatedAt = now;
        assignment.UpdatedBy = userId;
        await _db.SaveChangesAsync();
    }

    // ── Helpers ──────────────────────────────────────────────

    private async Task<Dictionary<Guid, DeviceVendor>> GetVendorMapAsync()
        => await _db.DeviceVendors.AsNoTracking()
            .Where(v => !v.IsDeleted)
            .ToDictionaryAsync(v => v.Id);

    private async Task<DeviceDto?> LoadDeviceDtoAsync(Guid deviceId, Dictionary<Guid, DeviceVendor> vendors)
    {
        var device = await _db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == deviceId);
        if (device == null) return null;

        var sims = await _db.DeviceSims.AsNoTracking()
            .Where(s => s.DeviceId == deviceId && !s.IsDeleted)
            .Select(s => ToSimDto(s))
            .ToListAsync();

        var assignment = await _db.VehicleDevices.AsNoTracking()
            .FirstOrDefaultAsync(vd => vd.DeviceId == deviceId && vd.AssignedTo == null && !vd.IsDeleted);
        string? reg = null;
        if (assignment != null)
        {
            var vehicle = await _db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == assignment.VehicleId);
            reg = vehicle?.RegistrationNumber;
        }

        vendors.TryGetValue(device.VendorId ?? Guid.Empty, out var vendor);
        return new DeviceDto
        {
            Id = device.Id,
            CompanyId = device.CompanyId,
            VendorId = device.VendorId,
            VendorCode = vendor?.Code,
            VendorName = vendor?.Name,
            DeviceType = (int)device.DeviceType,
            DeviceTypeOverride = device.DeviceTypeOverride,
            IdentityType = (int)device.IdentityType,
            IdentityValue = device.IdentityValue,
            Model = device.Model,
            FirmwareVersion = device.FirmwareVersion,
            Status = (int)device.Status,
            InstallDate = device.InstallDate,
            ActivatedAt = device.ActivatedAt,
            LastSeenAt = device.LastSeenAt,
            CreatedAt = device.CreatedAt,
            Sims = sims,
            CurrentVehicleId = assignment?.VehicleId,
            CurrentVehicleRegistration = reg
        };
    }

    private static DeviceSimDto ToSimDto(DeviceSim s) => new()
    {
        Id = s.Id,
        DeviceId = s.DeviceId,
        Iccid = s.Iccid,
        PhoneNumber = s.PhoneNumber,
        Carrier = s.Carrier,
        Status = (int)s.Status,
        IsPrimary = s.IsPrimary,
        ActivatedAt = s.ActivatedAt
    };
}
