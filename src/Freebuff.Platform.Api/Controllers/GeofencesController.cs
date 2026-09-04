using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Freebuff.Platform.Api.Authorization;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Shared.Models;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.CompanyScope;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Geofencing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class GeofencesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly TargetCompanyResolver _targetCompany;
    public GeofencesController(ApplicationDbContext db, ITenantContext tenant, TargetCompanyResolver targetCompany) { _db = db; _tenant = tenant; _targetCompany = targetCompany; }

    private Guid GetTenantId() => Guid.Parse(User.FindFirstValue("tenant_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsSuperAdmin() => User.IsInRole("SuperAdmin") || User.Claims.Any(c => c.Type == "is_super_admin" && c.Value == "true");
    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    [RequirePermission("geofence.view")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] int? status = null, [FromQuery] int? type = null,
        [FromQuery] string? sortBy = null, [FromQuery] bool sortDesc = false)
    {
        // Query-side: effective scope = X-Company-Scope ∩ permitted set (list view).
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var query = _db.Geofences.AsNoTracking()
            .Where(g => !g.IsDeleted && (scope == null || scope.Contains(g.CompanyId)))
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(g => g.Name.Contains(search) || (g.Description != null && g.Description.Contains(search)));
        if (status.HasValue) query = query.Where(g => (int)g.Status == status.Value);
        if (type.HasValue) query = query.Where(g => (int)g.Type == type.Value);

        // Server-side sorting
        query = sortBy?.ToLower() switch
        {
            "name" => sortDesc ? query.OrderByDescending(g => g.Name) : query.OrderBy(g => g.Name),
            "type" => sortDesc ? query.OrderByDescending(g => g.Type) : query.OrderBy(g => g.Type),
            "status" => sortDesc ? query.OrderByDescending(g => g.Status) : query.OrderBy(g => g.Status),
            _ => query.OrderByDescending(g => g.CreatedAt)
        };

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(g => new GeofenceDto
            {
                Id = g.Id, Name = g.Name, Description = g.Description,
                Type = (int)g.Type, TypeName = g.Type.ToString(),
                Status = (int)g.Status,
                CompanyName = g.Company.Name,
                Geometry = g.Geometry,
                Coordinates = g.Coordinates, CenterLatitude = g.CenterLatitude, CenterLongitude = g.CenterLongitude, Radius = g.Radius,
                FillColor = g.FillColor, BorderColor = g.BorderColor, BorderWidth = g.BorderWidth,
                AlertOnEntry = g.VehicleGeofences.Any() ? g.VehicleGeofences.First().AlertOnEntry : true,
                AlertOnExit = g.VehicleGeofences.Any() ? g.VehicleGeofences.First().AlertOnExit : true,
                AlertOnDwell = g.VehicleGeofences.Any() ? g.VehicleGeofences.First().AlertOnDwell : false,
                DwellTimeMinutes = g.VehicleGeofences.Any() ? g.VehicleGeofences.First().DwellTimeMinutes : null,
                AssignedVehicleCount = g.VehicleGeofences.Count,
                ViolationCount = g.ViolationCount,
                CreatedAt = g.CreatedAt
            }).ToListAsync();

        return Ok(new ApiResponse<PagedResult<GeofenceDto>>
        {
            Success = true,
            Data = new PagedResult<GeofenceDto> { Items = items, TotalCount = total, Page = page, PageSize = pageSize }
        });
    }

    [HttpGet("stats")]
    [RequirePermission("geofence.view")]
    public async Task<IActionResult> GetStats()
    {
        // Query-side: effective scope = X-Company-Scope ∩ permitted set.
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var query = _db.Geofences.AsNoTracking().Where(g => !g.IsDeleted && (scope == null || scope.Contains(g.CompanyId)));

        var assignmentsQuery = scope == null
            ? _db.VehicleGeofences.AsNoTracking().Where(vg => !vg.IsDeleted)
            : _db.VehicleGeofences.AsNoTracking().Where(vg => !vg.IsDeleted && scope.Contains(vg.Geofence.CompanyId));

        return Ok(new ApiResponse<object>
        {
            Success = true,
            Data = new
            {
                Total = await query.CountAsync(),
                Active = await query.CountAsync(g => g.Status == EntityStatus.Active),
                Inactive = await query.CountAsync(g => g.Status == EntityStatus.Inactive),
                Circles = await query.CountAsync(g => g.Type == GeofenceType.Circle),
                Rectangles = await query.CountAsync(g => g.Type == GeofenceType.Rectangle),
                Polygons = await query.CountAsync(g => g.Type == GeofenceType.Polygon),
                TotalAssignments = await assignmentsQuery.CountAsync(),
                TotalViolations = await query.SumAsync(g => g.ViolationCount)
            }
        });
    }

    [HttpGet("{id:guid}")]
    [RequirePermission("geofence.view")]
    public async Task<IActionResult> Get(Guid id)
    {
        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var g = await _db.Geofences.AsNoTracking()
            .Include(g => g.Company).Include(g => g.VehicleGeofences).ThenInclude(vg => vg.Vehicle)
            .Include(g => g.VehicleGeofences).ThenInclude(vg => vg.Driver)
            .FirstOrDefaultAsync(g => g.Id == id && !g.IsDeleted && (isSuperAdmin || g.CompanyId == tenantId));

        if (g == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Geofence not found." });

        var vg = g.VehicleGeofences.FirstOrDefault();
        return Ok(new ApiResponse<GeofenceDto>
        {
            Success = true,
            Data = new GeofenceDto
            {
                Id = g.Id, Name = g.Name, Description = g.Description,
                Type = (int)g.Type, TypeName = g.Type.ToString(),
                Status = (int)g.Status,
                CompanyName = g.Company.Name,
                Geometry = g.Geometry,
                Coordinates = g.Coordinates, CenterLatitude = g.CenterLatitude, CenterLongitude = g.CenterLongitude, Radius = g.Radius,
                FillColor = g.FillColor, BorderColor = g.BorderColor, BorderWidth = g.BorderWidth,
                AlertOnEntry = vg?.AlertOnEntry ?? true, AlertOnExit = vg?.AlertOnExit ?? true,
                AlertOnDwell = vg?.AlertOnDwell ?? false, DwellTimeMinutes = vg?.DwellTimeMinutes,
                AssignedVehicleCount = g.VehicleGeofences.Count,
                ViolationCount = g.ViolationCount,
                CreatedAt = g.CreatedAt
            }
        });
    }

    [HttpPost]
    [RequirePermission("geofence.create")]
    public async Task<IActionResult> Create([FromBody] CreateGeofenceDto dto)
    {

        // SuperAdmin must name the target company; company users are always
        // forced to their own tenant server-side.
        var companyId = await _targetCompany.ResolveAsync(dto.CompanyId);
        var g = new Geofence
        {
            Id = Guid.NewGuid(), Name = dto.Name, Description = dto.Description,
            Type = (GeofenceType)dto.Type, Status = EntityStatus.Active,
            Coordinates = dto.Coordinates ?? string.Empty,
            CenterLatitude = dto.CenterLatitude, CenterLongitude = dto.CenterLongitude, Radius = dto.Radius,
            FillColor = dto.FillColor, BorderColor = dto.BorderColor, BorderWidth = dto.BorderWidth,
            CompanyId = companyId, TenantId = companyId
        };

        // Shape resolution: canonical GeoJSON geometry wins; otherwise fall back
        // to the legacy radius-based circle flow. Rectangles are no longer
        // created — a rectangle is just a 4-point polygon.
        var shapeError = ResolveShape(g, dto.Geometry, (GeofenceType)dto.Type,
            dto.CenterLatitude, dto.CenterLongitude, dto.Radius, dto.Coordinates);
        if (shapeError != null)
            return BadRequest(new ApiResponse<object> { Success = false, Message = shapeError });

        _db.Geofences.Add(g);

        if (dto.AssignedVehicleIds != null && dto.AssignedVehicleIds.Any())
        {
            // Only assign vehicles that belong to the target company
            var validVehicleIds = await _db.Vehicles
                .Where(v => dto.AssignedVehicleIds.Contains(v.Id) && !v.IsDeleted && v.CompanyId == companyId)
                .Select(v => v.Id)
                .ToListAsync();
            foreach (var vid in validVehicleIds)
            {
                _db.VehicleGeofences.Add(new VehicleGeofence
                {
                    Id = Guid.NewGuid(), GeofenceId = g.Id, VehicleId = vid,
                    AlertOnEntry = dto.AlertOnEntry, AlertOnExit = dto.AlertOnExit,
                    AlertOnDwell = dto.AlertOnDwell, DwellTimeMinutes = dto.DwellTimeMinutes,
                    TenantId = companyId
                });
            }
        }

        await _db.SaveChangesAsync();
        _targetCompany.Audit(AuditAction.Create, EntityType.Geofence, g.Id, g.Name, null, companyId);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = g.Id }, new ApiResponse<object> { Success = true, Message = "Geofence created." });
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("geofence.update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateGeofenceDto dto)
    {

        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var g = await _db.Geofences.FirstOrDefaultAsync(g => g.Id == id && !g.IsDeleted && (isSuperAdmin || g.CompanyId == tenantId));
        if (g == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Geofence not found." });

        if (dto.Name != null) g.Name = dto.Name;
        if (dto.Description != null) g.Description = dto.Description;
        if (dto.Status.HasValue) g.Status = (EntityStatus)dto.Status.Value;
        if (dto.FillColor != null) g.FillColor = dto.FillColor;
        if (dto.BorderColor != null) g.BorderColor = dto.BorderColor;
        if (dto.BorderWidth.HasValue) g.BorderWidth = dto.BorderWidth.Value;

        // Shape update: an explicit geometry (draw-on-map / import) replaces the
        // shape wholesale. A full flat circle payload (legacy form clients)
        // rebuilds the canonical circle. Anything else leaves the stored shape
        // untouched — stale flat fields must never half-update canonical data.
        var flatCircle = dto.CenterLatitude.HasValue && dto.CenterLongitude.HasValue && dto.Radius.HasValue;
        if (dto.Geometry != null)
        {
            var err = GeofenceShapeMapper.ApplyGeometry(g, dto.Geometry);
            if (err != null)
                return BadRequest(new ApiResponse<object> { Success = false, Message = err });
        }
        else if (flatCircle)
        {
            var err = GeofenceShapeMapper.ApplyLegacyCircle(g, dto.CenterLatitude, dto.CenterLongitude, dto.Radius);
            if (err != null)
                return BadRequest(new ApiResponse<object> { Success = false, Message = err });
        }

        var vgs = await _db.VehicleGeofences.Where(vg => vg.GeofenceId == id && !vg.IsDeleted).ToListAsync();
        foreach (var vg in vgs)
        {
            if (dto.AlertOnEntry.HasValue) vg.AlertOnEntry = dto.AlertOnEntry.Value;
            if (dto.AlertOnExit.HasValue) vg.AlertOnExit = dto.AlertOnExit.Value;
            if (dto.AlertOnDwell.HasValue) vg.AlertOnDwell = dto.AlertOnDwell.Value;
            if (dto.DwellTimeMinutes.HasValue) vg.DwellTimeMinutes = dto.DwellTimeMinutes.Value;
        }

        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<object> { Success = true, Message = "Geofence updated." });
    }

    /// <summary>
    /// Bulk import — CSV (radius circles) or GeoJSON FeatureCollection (circles
    /// as Points with properties.radiusMeters, polygons as Polygon features).
    /// Every row/feature passes the same validation as manual drawing; invalid
    /// rows are reported individually and never fail the whole batch.
    /// </summary>
    [HttpPost("import")]
    [RequirePermission("geofence.import")]
    public async Task<IActionResult> Import([FromBody] ImportGeofencesDto dto)
    {
        var companyId = await _targetCompany.ResolveAsync(dto.CompanyId);
        var failures = new List<object>();
        var fences = new List<Geofence>();

        try
        {
            if (string.Equals(dto.Format, "geojson", StringComparison.OrdinalIgnoreCase))
                ParseGeoJsonImport(dto.Content, companyId, fences, failures);
            else
                ParseCsvImport(dto.Content, companyId, fences, failures);
        }
        catch (JsonException ex)
        {
            return BadRequest(new ApiResponse<object> { Success = false, Message = $"Import is not valid: {ex.Message}" });
        }

        _db.Geofences.AddRange(fences);
        await _db.SaveChangesAsync();
        foreach (var g in fences)
            _targetCompany.Audit(AuditAction.Create, EntityType.Geofence, g.Id, g.Name, null, companyId);
        await _db.SaveChangesAsync();

        return Ok(new ApiResponse<object>
        {
            Success = true,
            Data = new { Imported = fences.Count, Failed = failures.Count, Errors = failures, Total = fences.Count + failures.Count }
        });
    }

    private void ParseGeoJsonImport(string content, Guid companyId, List<Geofence> fences, List<object> failures)
    {
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        var features = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().ToList()
            : (root.TryGetProperty("features", out var fs) ? fs.EnumerateArray().ToList() : new List<JsonElement>());
        if (features.Count == 0)
        {
            failures.Add(new { Row = 1, Error = "No features found in the GeoJSON." });
            return;
        }
        for (var i = 0; i < features.Count; i++)
        {
            var feat = features[i];
            var label = $"feature #{i + 1}";
            string? name = null, description = null;
            if (feat.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            {
                if (props.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) name = n.GetString();
                if (props.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String) description = d.GetString();
            }
            name = string.IsNullOrWhiteSpace(name) ? $"Imported Geofence {i + 1}" : name!;

            if (!feat.TryGetProperty("geometry", out var geom) || geom.ValueKind != JsonValueKind.Object)
            {
                failures.Add(new { Row = label, Error = "Feature has no geometry object." });
                continue;
            }
            var gtype = geom.TryGetProperty("type", out var gt) ? gt.GetString() : null;
            var candidate = new Geofence { Id = Guid.NewGuid(), Name = name, Description = description, Status = EntityStatus.Active, CompanyId = companyId, TenantId = companyId };
            string? shapeError = null;
            switch (gtype)
            {
                case "Polygon":
                {
                    // Outer ring only — holes are rejected per feature.
                    var rings = geom.TryGetProperty("coordinates", out var coords) && coords.ValueKind == JsonValueKind.Array
                        ? coords.EnumerateArray().ToList() : new List<JsonElement>();
                    if (rings.Count == 0) shapeError = "Polygon has no rings.";
                    else if (rings.Count > 1) shapeError = "Polygons with holes are not supported.";
                    else if (rings[0].GetArrayLength() < 3) shapeError = "A polygon needs at least 3 points.";
                    else
                    {
                        var json = $"{{\"type\":\"polygon\",\"coordinates\":{rings[0].GetRawText()}}}";
                        shapeError = GeofenceShapeMapper.ApplyGeometry(candidate, json);
                    }
                    break;
                }
                case "Point":
                {
                    // Circle from a point + properties.radiusMeters.
                    var radius = props.TryGetProperty("radiusMeters", out var rm) && rm.ValueKind == JsonValueKind.Number ? rm.GetDouble() : 0;
                    var json = $"{{\"type\":\"circle\",\"center\":{geom.GetProperty("coordinates").GetRawText()},\"radiusMeters\":{JsonSerializer.Serialize(radius)}}}";
                    shapeError = GeofenceShapeMapper.ApplyGeometry(candidate, json);
                    break;
                }
                default:
                    shapeError = "Unsupported GeoJSON geometry type (use Polygon or Point).";
                    break;
            }
            if (shapeError != null) failures.Add(new { Row = label, Error = shapeError });
            else fences.Add(candidate);
        }
    }

    private void ParseCsvImport(string content, Guid companyId, List<Geofence> fences, List<object> failures)
    {
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            failures.Add(new { Row = 1, Error = "CSV is empty." });
            return;
        }
        var start = 0;
        // Skip a header row when present.
        if (lines[0].Contains("name", StringComparison.OrdinalIgnoreCase)
            && (lines[0].Contains("lat", StringComparison.OrdinalIgnoreCase) || lines[0].Contains("radius", StringComparison.OrdinalIgnoreCase)))
            start = 1;
        for (var i = start; i < lines.Length; i++)
        {
            var cols = lines[i].Split(',').Select(c => c.Trim()).ToArray();
            if (cols.Length < 3)
            {
                failures.Add(new { Row = i + 1, Error = "Expected: name,latitude,longitude,radiusMeters" });
                continue;
            }
            if (!double.TryParse(cols[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                || !double.TryParse(cols[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
            {
                failures.Add(new { Row = i + 1, Error = "Latitude/longitude are not valid numbers." });
                continue;
            }
            var radius = cols.Length > 3 && double.TryParse(cols[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 0;
            var name = cols[0];
            var candidate = new Geofence { Id = Guid.NewGuid(), Name = name, Description = cols.Length > 4 ? cols[4] : null, Status = EntityStatus.Active, CompanyId = companyId, TenantId = companyId };
            var json = $"{{\"type\":\"circle\",\"center\":[{JsonSerializer.Serialize(lng)},{JsonSerializer.Serialize(lat)}],\"radiusMeters\":{JsonSerializer.Serialize(radius)}}}";
            var err = GeofenceShapeMapper.ApplyGeometry(candidate, json);
            if (err != null) failures.Add(new { Row = i + 1, Error = err });
            else fences.Add(candidate);
        }
    }

    /// <summary>Resolves the shape for a create — geometry, legacy circle, or (legacy) polygon ring. Rectangle is retired.</summary>
    private static string? ResolveShape(Geofence g, string? geometry, GeofenceType type,
        double? centerLat, double? centerLng, double? radius, string? coordinates)
    {
        if (!string.IsNullOrWhiteSpace(geometry))
            return GeofenceShapeMapper.ApplyGeometry(g, geometry);
        if (type == GeofenceType.Rectangle)
            return "Rectangle geofences are no longer supported — draw a circle or polygon instead.";
        if (type == GeofenceType.Circle)
            return GeofenceShapeMapper.ApplyLegacyCircle(g, centerLat, centerLng, radius);
        // Legacy polygon path: coordinates must be a GeoJSON ring [[lng,lat],...].
        var ringError = "Polygon geofences require a GeoJSON geometry — draw it on the map or import GeoJSON.";
        if (string.IsNullOrWhiteSpace(coordinates)) return ringError;
        var asJson = coordinates.TrimStart().StartsWith("{")
            ? coordinates
            : $"{{\"type\":\"polygon\",\"coordinates\":{coordinates}}}";
        return GeofenceShapeMapper.ApplyGeometry(g, asJson) ?? null;
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("geofence.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {

        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var g = await _db.Geofences.FirstOrDefaultAsync(g => g.Id == id && !g.IsDeleted && (isSuperAdmin || g.CompanyId == tenantId));
        if (g == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Geofence not found." });

        g.IsDeleted = true;
        g.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<object> { Success = true, Message = "Geofence deleted." });
    }

    [HttpPost("{id:guid}/restore")]
    [RequirePermission("geofence.update")]
    public async Task<IActionResult> Restore(Guid id)
    {

        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var g = await _db.Geofences.FirstOrDefaultAsync(g => g.Id == id && g.IsDeleted && (isSuperAdmin || g.CompanyId == tenantId));
        if (g == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Geofence not found." });

        g.IsDeleted = false;
        g.DeletedAt = null;
        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<object> { Success = true, Message = "Geofence restored." });
    }

    [HttpPost("{id:guid}/assign-vehicle")]
    [RequirePermission("geofence.update")]
    public async Task<IActionResult> AssignVehicle(Guid id, [FromBody] AssignVehicleGeofenceDto dto)
    {

        var tenantId = GetTenantId();
        var isSuperAdmin = IsSuperAdmin();
        var g = await _db.Geofences.FirstOrDefaultAsync(g => g.Id == id && !g.IsDeleted && (isSuperAdmin || g.CompanyId == tenantId));
        if (g == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Geofence not found." });

        // Validate vehicle belongs to the same company
        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == dto.VehicleId && !v.IsDeleted && v.CompanyId == tenantId);
        if (vehicle == null) return NotFound(new ApiResponse<object> { Success = false, Message = "Vehicle not found in your company." });

        var exists = await _db.VehicleGeofences.AnyAsync(vg => vg.GeofenceId == id && vg.VehicleId == dto.VehicleId && !vg.IsDeleted);
        if (exists) return BadRequest(new ApiResponse<object> { Success = false, Message = "Vehicle already assigned to this geofence." });

        _db.VehicleGeofences.Add(new VehicleGeofence
        {
            Id = Guid.NewGuid(), GeofenceId = id, VehicleId = dto.VehicleId,
            DriverId = dto.DriverId,
            AlertOnEntry = dto.AlertOnEntry ?? true, AlertOnExit = dto.AlertOnExit ?? true,
            AlertOnDwell = dto.AlertOnDwell ?? false, DwellTimeMinutes = dto.DwellTimeMinutes,
            TenantId = tenantId
        });
        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<object> { Success = true, Message = "Vehicle assigned to geofence." });
    }
}

public class AssignVehicleGeofenceDto
{
    public Guid VehicleId { get; set; }
    public Guid? DriverId { get; set; }
    public bool? AlertOnEntry { get; set; }
    public bool? AlertOnExit { get; set; }
    public bool? AlertOnDwell { get; set; }
    public int? DwellTimeMinutes { get; set; }
}
