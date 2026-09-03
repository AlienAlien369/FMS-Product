using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Shared.Extensions;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "SuperAdmin")]
public class AdminController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public AdminController(ApplicationDbContext db) => _db = db;

    // ── Platform Overview ───────────────────────────────
    [HttpGet("overview")]
    public async Task<ActionResult<ApiResponse<object>>> GetOverview()
    {
        var companies = await _db.Companies.CountAsync(c => !c.IsDeleted);
        var users = await _db.Users.CountAsync(u => !u.IsDeleted);
        var vehicles = await _db.Vehicles.CountAsync(v => !v.IsDeleted);
        var drivers = await _db.Drivers.CountAsync(d => !d.IsDeleted);
        var roles = await _db.Roles.CountAsync(r => !r.IsDeleted);
        var modules = await _db.Modules.CountAsync(m => !m.IsDeleted);

        return Ok(ApiResponse<object>.Ok(new
        {
            companies, users, vehicles, drivers, roles, modules
        }));
    }

    // ── All Companies with Stats ────────────────────────
    [HttpGet("companies")]
    public async Task<ActionResult<ApiResponse<PagedResult<object>>>> GetAllCompanies([FromQuery] PagedRequest filter)
    {
        var query = _db.Companies.AsNoTracking().Where(c => !c.IsDeleted);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(c => c.Name.Contains(filter.Search) || (c.Slug != null && c.Slug.Contains(filter.Search))
                || (c.ContactEmail != null && c.ContactEmail.Contains(filter.Search))
                || (c.Country != null && c.Country.Contains(filter.Search)));

        query = query.OrderBy(c => c.Name);

        var total = await query.CountAsync();
        var companyIds = await query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).Select(c => c.Id).ToListAsync();

        var userCounts = await _db.Users.AsNoTracking().Where(u => companyIds.Contains(u.CompanyId) && !u.IsDeleted)
            .GroupBy(u => u.CompanyId).Select(g => new { CompanyId = g.Key, Count = g.Count() }).ToListAsync();
        var vehicleCounts = await _db.Vehicles.AsNoTracking().Where(v => companyIds.Contains(v.CompanyId) && !v.IsDeleted)
            .GroupBy(v => v.CompanyId).Select(g => new { CompanyId = g.Key, Count = g.Count() }).ToListAsync();
        var driverCounts = await _db.Drivers.AsNoTracking().Where(d => companyIds.Contains(d.CompanyId) && !d.IsDeleted)
            .GroupBy(d => d.CompanyId).Select(g => new { CompanyId = g.Key, Count = g.Count() }).ToListAsync();
        var roleCounts = await _db.Roles.AsNoTracking().Where(r => companyIds.Contains(r.CompanyId) && !r.IsDeleted)
            .GroupBy(r => r.CompanyId).Select(g => new { CompanyId = g.Key, Count = g.Count() }).ToListAsync();
        // Get subscription info for each company
        var subscriptions = await _db.Subscriptions.AsNoTracking()
            .Where(s => companyIds.Contains(s.CompanyId) && !s.IsDeleted && s.Status == SubscriptionStatus.Active)
            .Select(s => new { s.CompanyId, s.PackageId, Status = (int)s.Status, s.EndDate })
            .ToListAsync();
        var packageNames = await _db.Packages.AsNoTracking()
            .Where(p => subscriptions.Select(s => s.PackageId).Contains(p.Id) && !p.IsDeleted)
            .Select(p => new { p.Id, p.Name, p.Price })
            .ToListAsync();

        // Module count comes from the company's package (modules granted), not from
        // per-company module rows.
        var relevantPackageIds = subscriptions.Select(s => s.PackageId).Distinct().ToList();
        var moduleCounts = await _db.PackageModules.AsNoTracking()
            .Where(pm => relevantPackageIds.Contains(pm.PackageId) && !pm.IsDeleted)
            .GroupBy(pm => pm.PackageId)
            .Select(g => new { PackageId = g.Key, Count = g.Count() })
            .ToListAsync();

        var allCompanies = await query.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(c => new { c.Id, c.Name, c.Slug, c.LogoUrl, c.ContactEmail, c.ContactPhone,
                c.Country, c.City, c.Website, c.Address,
                Status = (int)c.Status, c.CreatedAt,
                c.DefaultLanguage, c.DefaultTimezone, c.DefaultCurrency,
                c.PackageId
            }).ToListAsync();

        var items = allCompanies.Select(c =>
        {
            var sub = subscriptions.FirstOrDefault(s => s.CompanyId == c.Id);
            var pkg = sub != null ? packageNames.FirstOrDefault(p => p.Id == sub.PackageId) : null;
            var isExpired = sub?.EndDate != null && sub.EndDate < DateTime.UtcNow;
            var packageId = sub?.PackageId ?? c.PackageId;
            return (object)new
            {
                c.Id, c.Name, c.Slug, c.LogoUrl, c.ContactEmail, c.ContactPhone,
                c.Country, c.City, c.Website, c.Address,
                c.Status, c.CreatedAt, c.DefaultLanguage, c.DefaultTimezone, c.DefaultCurrency,
                UserCount = userCounts.FirstOrDefault(u => u.CompanyId == c.Id)?.Count ?? 0,
                VehicleCount = vehicleCounts.FirstOrDefault(v => v.CompanyId == c.Id)?.Count ?? 0,
                DriverCount = driverCounts.FirstOrDefault(d => d.CompanyId == c.Id)?.Count ?? 0,
                RoleCount = roleCounts.FirstOrDefault(r => r.CompanyId == c.Id)?.Count ?? 0,
                ModuleCount = packageId != null ? (moduleCounts.FirstOrDefault(mc => mc.PackageId == packageId)?.Count ?? 0) : 0,
                SubscriptionStatus = sub?.Status,
                PackageName = pkg?.Name,
                PackagePrice = pkg?.Price,
                SubscriptionEndDate = sub?.EndDate,
                IsSubscriptionExpired = isExpired
            };
        }).ToList();

        return Ok(ApiResponse<PagedResult<object>>.Ok(new PagedResult<object>
        {
            Items = items,
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        }));
    }

    // ── Company Detail ──────────────────────────────────
    [HttpGet("companies/{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> GetCompanyDetail(Guid id)
    {
        var company = await _db.Companies.AsNoTracking()
            .Where(c => c.Id == id && !c.IsDeleted)
            .Select(c => new
            {
                c.Id, c.Name, c.Slug, c.LogoUrl, c.FaviconUrl,
                c.ContactEmail, c.ContactPhone, c.Website,
                c.Address, c.City, c.State, c.Country, c.PostalCode,
                Status = (int)c.Status, c.CreatedAt,
                c.DefaultLanguage, c.DefaultTimezone, c.DefaultCurrency,
                c.DateFormat, c.TimeFormat, c.NumberFormat,
                MapProvider = (int)c.DefaultMapProvider, c.MapApiKey,
                UserCount = c.Users.Count(u => !u.IsDeleted),
                VehicleCount = c.Vehicles.Count(v => !v.IsDeleted),
                DriverCount = c.Drivers.Count(d => !d.IsDeleted),
                RoleCount = c.Roles.Count(r => !r.IsDeleted)
            })
            .FirstOrDefaultAsync();

        if (company == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Company not found"));
        return Ok(ApiResponse<object>.Ok(company));
    }

    // ── Company Users ───────────────────────────────────
    [HttpGet("companies/{id:guid}/users")]
    public async Task<ActionResult<ApiResponse<object>>> GetCompanyUsers(Guid id, [FromQuery] PagedRequest filter)
    {
        var query = _db.Users.AsNoTracking()
            .Where(u => u.CompanyId == id && !u.IsDeleted);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(u => u.Email.Contains(filter.Search) || u.FirstName.Contains(filter.Search));

        var total = await query.CountAsync();
        var items = await query
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(u => new
            {
                u.Id, u.Email, u.FirstName, u.LastName, u.PhoneNumber,
                Status = (int)u.Status, u.LastLoginAt, u.CreatedAt,
                Roles = u.UserRoles.Where(ur => !ur.IsDeleted).Select(ur => ur.Role.Name).ToList(),
                RoleIds = u.UserRoles.Where(ur => !ur.IsDeleted).Select(ur => ur.RoleId).ToList()
            }).ToListAsync();

        return Ok(ApiResponse<object>.Ok(new PagedResult<object>
        {
            Items = items.Cast<object>().ToList(),
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        }));
    }

    // ── Company Roles ───────────────────────────────────
    [HttpGet("companies/{id:guid}/roles")]
    public async Task<ActionResult<ApiResponse<object>>> GetCompanyRoles(Guid id)
    {
        var roles = await _db.Roles.AsNoTracking()
            .Where(r => r.CompanyId == id && !r.IsDeleted)
            .OrderBy(r => r.Name)
            .Select(r => new
            {
                r.Id, r.Name, r.Description, r.IsSystemRole,
                Status = (int)r.Status,
                UserCount = r.UserRoles.Count(ur => !ur.IsDeleted),
                PermissionCount = r.RolePermissions.Count(rp => !rp.IsDeleted),
                Permissions = r.RolePermissions.Where(rp => !rp.IsDeleted)
                    .Select(rp => new { rp.Permission.Code, rp.Permission.Name, Module = rp.Permission.Module })
                    .ToList()
            }).ToListAsync();

        return Ok(ApiResponse<object>.Ok(roles));
    }

    // ── Company Modules (derived from the company's package) ────────────────
    // There is no per-company module override. A company can use exactly the
    // modules its package grants; page-level access inside a module is RBAC.
    [HttpGet("companies/{id:guid}/modules")]
    public async Task<ActionResult<ApiResponse<object>>> GetCompanyModules(Guid id)
    {
        var company = await _db.Companies.AsNoTracking()
            .Where(c => c.Id == id && !c.IsDeleted)
            .Select(c => new { c.PackageId, c.SubscriptionId })
            .FirstOrDefaultAsync();
        if (company == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Company not found"));

        var packageId = company.PackageId;
        if (packageId == null && company.SubscriptionId != null)
        {
            packageId = await _db.Subscriptions.AsNoTracking()
                .Where(s => s.Id == company.SubscriptionId && !s.IsDeleted && s.Status == SubscriptionStatus.Active)
                .Select(s => (Guid?)s.PackageId)
                .FirstOrDefaultAsync();
        }

        string? packageName = null;
        HashSet<Guid> includedModuleIds = new();
        if (packageId != null)
        {
            var pkg = await _db.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == packageId.Value && !p.IsDeleted);
            packageName = pkg?.Name;
            var granted = await _db.PackageModules.AsNoTracking()
                .Where(pm => pm.PackageId == packageId.Value && !pm.IsDeleted)
                .Select(pm => pm.ModuleId)
                .ToListAsync();
            includedModuleIds = granted.ToHashSet();
        }
        else
        {
            // Legacy companies without a package keep their historical rows until a package is assigned.
            var legacy = await _db.ModuleConfigurations.AsNoTracking()
                .Where(mc => mc.CompanyId == id && !mc.IsDeleted && mc.Status == EntityStatus.Active)
                .Select(mc => mc.ModuleId)
                .ToListAsync();
            includedModuleIds = legacy.ToHashSet();
        }

        var modules = await _db.Modules.AsNoTracking()
            .Where(m => !m.IsDeleted)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync();

        var result = modules.Select(m =>
        {
            var pages = PageRegistry.PagesInModule(m.Code).ToList();
            return new
            {
                m.Id, m.Code, m.Name, m.Description, m.Icon, m.IsCore,
                Status = (int)m.Status, m.DisplayOrder,
                Included = includedModuleIds.Contains(m.Id),
                PageCount = pages.Count(p => !p.Planned),
                PlannedPageCount = pages.Count(p => p.Planned),
                Pages = pages.Select(p => new { p.Key, p.Label, p.Planned, p.Nav, p.Route }).ToList()
            };
        }).ToList();

        return Ok(ApiResponse<object>.Ok(new
        {
            PackageId = packageId,
            PackageName = packageName,
            IncludedModuleCodes = result.Where(r => r.Included).Select(r => r.Code).ToList(),
            Modules = result
        }));
    }

    // ── Company Documents ───────────────────────────────
    [HttpGet("companies/{id:guid}/documents")]
    public async Task<ActionResult<ApiResponse<object>>> GetCompanyDocuments(Guid id, [FromQuery] PagedRequest filter)
    {
        var query = _db.Documents.AsNoTracking()
            .Where(d => d.CompanyId == id && !d.IsDeleted);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(d => d.FileName.Contains(filter.Search) || (d.Category != null && d.Category.Contains(filter.Search)));

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(d => new
            {
                d.Id, d.FileName, d.OriginalFileName, d.ContentType,
                FileSize = d.FileSize, d.Category, d.ExpiryDate,
                EntityType = d.EntityType.ToString(), d.EntityId,
                Status = (int)d.Status, d.CreatedAt
            }).ToListAsync();

        return Ok(ApiResponse<object>.Ok(new PagedResult<object>
        {
            Items = items.Cast<object>().ToList(),
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        }));
    }

    // ── Platform Modules (all) — module groups with their pages ─────────────
    [HttpGet("modules")]
    public async Task<ActionResult<ApiResponse<object>>> GetAllModules()
    {
        var modules = await _db.Modules.AsNoTracking()
            .Where(m => !m.IsDeleted)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync();

        var result = modules.Select(m =>
        {
            var pages = PageRegistry.PagesInModule(m.Code).ToList();
            return new
            {
                m.Id, m.Code, m.Name, m.Description, m.Icon, m.IsCore, m.DisplayOrder,
                Status = (int)m.Status,
                PageCount = pages.Count(p => !p.Planned),
                PlannedPageCount = pages.Count(p => p.Planned),
                Pages = pages.Select(p => new { p.Key, p.Label, p.Planned, p.Nav, p.Route }).ToList()
            };
        }).ToList();

        return Ok(ApiResponse<object>.Ok(result));
    }

    // ── Platform Permissions (all) ──────────────────────
    [HttpGet("permissions")]
    public async Task<ActionResult<ApiResponse<object>>> GetAllPermissions()
    {
        var permissions = await _db.Permissions.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .GroupBy(p => p.Module)
            .Select(g => new
            {
                Module = g.Key,
                Permissions = g.Select(p => new { p.Id, p.Code, p.Name, Action = p.Action.ToString() }).ToList(),
                RoleCount = g.SelectMany(p => p.RolePermissions.Where(rp => !rp.IsDeleted)).Count()
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(permissions));
    }

    // ── Edit Company ─────────────────────────────────────
    [HttpPut("companies/{id:guid}")]
    public async Task<ActionResult<ApiResponse>> UpdateCompany(Guid id, [FromBody] UpdateCompanyDto dto)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
        if (company == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Company not found"));

        var localeErr = await ValidateCompanyLocaleAsync(dto.DefaultLanguage, dto.DefaultCurrency);
        if (localeErr != null) return BadRequest(ApiResponse.Fail("INVALID_LOCALE", localeErr));

        if (dto.Name != null) company.Name = dto.Name;
        if (dto.ContactEmail != null) company.ContactEmail = dto.ContactEmail;
        if (dto.ContactPhone != null) company.ContactPhone = dto.ContactPhone;
        if (dto.Country != null) company.Country = dto.Country;
        if (dto.DefaultLanguage != null) company.DefaultLanguage = dto.DefaultLanguage;
        if (dto.DefaultTimezone != null) company.DefaultTimezone = dto.DefaultTimezone;
        if (dto.DefaultCurrency != null) company.DefaultCurrency = dto.DefaultCurrency;
        if (dto.Status.HasValue) company.Status = (EntityStatus)dto.Status.Value;

        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "Company updated"));
    }

    // ── Create User in Company ───────────────────────────
    [HttpPost("companies/{id:guid}/users")]
    public async Task<ActionResult<ApiResponse<object>>> CreateUser(Guid id, [FromBody] CreateUserDto dto)
    {
        if (await _db.Users.AnyAsync(u => u.NormalizedEmail == dto.Email.ToUpperInvariant() && !u.IsDeleted))
            return BadRequest(ApiResponse.Fail("DUPLICATE_EMAIL", "A user with this email already exists"));

        var user = new User
        {
            Id = Guid.NewGuid(), Email = dto.Email, NormalizedEmail = dto.Email.ToUpperInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            FirstName = dto.FirstName, LastName = dto.LastName, PhoneNumber = dto.PhoneNumber,
            CompanyId = id, SecurityStamp = Guid.NewGuid().ToString(), Status = EntityStatus.Active, EmailConfirmed = true
        };
        _db.Users.Add(user);

        if (dto.RoleIds?.Any() == true)
        {
            foreach (var roleId in dto.RoleIds)
                _db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = roleId, TenantId = id });
        }
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { user.Id, user.Email }));
    }

    // ── Edit User in Company ─────────────────────────────
    [HttpPut("companies/{cid:guid}/users/{uid:guid}")]
    public async Task<ActionResult<ApiResponse>> UpdateUser(Guid cid, Guid uid, [FromBody] UpdateUserDto dto)
    {
        var user = await _db.Users.Include(u => u.UserRoles).FirstOrDefaultAsync(u => u.Id == uid && !u.IsDeleted && u.CompanyId == cid);
        if (user == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "User not found"));

        if (dto.FirstName != null) user.FirstName = dto.FirstName;
        if (dto.LastName != null) user.LastName = dto.LastName;
        if (dto.PhoneNumber != null) user.PhoneNumber = dto.PhoneNumber;
        if (dto.Status.HasValue) user.Status = (EntityStatus)dto.Status.Value;

        if (dto.RoleIds != null)
        {
            var existing = user.UserRoles.Where(ur => !ur.IsDeleted).ToList();
            _db.UserRoles.RemoveRange(existing);
            foreach (var roleId in dto.RoleIds)
                _db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = uid, RoleId = roleId, TenantId = cid });
        }
        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "User updated"));
    }

    // ── Delete User in Company ───────────────────────────
    [HttpDelete("companies/{cid:guid}/users/{uid:guid}")]
    public async Task<ActionResult<ApiResponse>> DeleteUser(Guid cid, Guid uid)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid && !u.IsDeleted && u.CompanyId == cid);
        if (user == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "User not found"));
        user.IsDeleted = true; user.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "User deleted"));
    }

    // ── Create Role in Company ───────────────────────────
    [HttpPost("companies/{id:guid}/roles")]
    public async Task<ActionResult<ApiResponse<object>>> CreateRole(Guid id, [FromBody] CreateRoleDto dto)
    {
        var role = new Role
        {
            Id = Guid.NewGuid(), Name = dto.Name, Description = dto.Description,
            CompanyId = id, Status = EntityStatus.Active, IsSystemRole = false
        };
        _db.Roles.Add(role);

        if (dto.PermissionIds?.Any() == true)
        {
            foreach (var permId in dto.PermissionIds)
                _db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = permId, TenantId = id });
        }
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { role.Id, role.Name }));
    }

    // ── Edit Role in Company ─────────────────────────────
    [HttpPut("companies/{cid:guid}/roles/{rid:guid}")]
    public async Task<ActionResult<ApiResponse>> UpdateRole(Guid cid, Guid rid, [FromBody] UpdateRoleDto dto)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == rid && !r.IsDeleted && r.CompanyId == cid);
        if (role == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Role not found"));
        if (role.IsSystemRole) return BadRequest(ApiResponse.Fail("FORBIDDEN", "System roles cannot be modified"));

        if (dto.Name != null) role.Name = dto.Name;
        if (dto.Description != null) role.Description = dto.Description;

        if (dto.PermissionIds != null)
        {
            var existing = await _db.RolePermissions.Where(rp => rp.RoleId == rid && !rp.IsDeleted).ToListAsync();
            _db.RolePermissions.RemoveRange(existing);
            foreach (var permId in dto.PermissionIds)
                _db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = rid, PermissionId = permId, TenantId = cid });
        }
        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "Role updated"));
    }

    // ── Delete Role in Company ───────────────────────────
    [HttpDelete("companies/{cid:guid}/roles/{rid:guid}")]
    public async Task<ActionResult<ApiResponse>> DeleteRole(Guid cid, Guid rid)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == rid && !r.IsDeleted && r.CompanyId == cid);
        if (role == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Role not found"));
        if (role.IsSystemRole) return BadRequest(ApiResponse.Fail("FORBIDDEN", "System roles cannot be deleted"));
        role.IsDeleted = true; role.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "Role deleted"));
    }

    // ── Create Company ───────────────────────────────────
    // New companies start from the platform defaults for language/currency and
    // optionally receive a package immediately (which defines their modules).
    [HttpPost("companies")]
    public async Task<ActionResult<ApiResponse<object>>> CreateCompany([FromBody] CreateCompanyDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(ApiResponse.Fail("VALIDATION", "Company name is required"));
        if (await _db.Companies.AnyAsync(c => c.Name == dto.Name && !c.IsDeleted))
            return BadRequest(ApiResponse.Fail("DUPLICATE", "A company with this name already exists"));
        if (!string.IsNullOrWhiteSpace(dto.Slug) && await _db.Companies.AnyAsync(c => c.Slug == dto.Slug && !c.IsDeleted))
            return BadRequest(ApiResponse.Fail("DUPLICATE", "A company with this slug already exists"));

        var localeErr = await ValidateCompanyLocaleAsync(dto.DefaultLanguage, dto.DefaultCurrency);
        if (localeErr != null) return BadRequest(ApiResponse.Fail("INVALID_LOCALE", localeErr));

        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Slug = string.IsNullOrWhiteSpace(dto.Slug) ? null : dto.Slug,
            ContactEmail = dto.ContactEmail,
            ContactPhone = dto.ContactPhone,
            Website = dto.Website,
            Address = dto.Address,
            City = dto.City,
            State = dto.State,
            Country = dto.Country,
            PostalCode = dto.PostalCode,
            DefaultLanguage = string.IsNullOrWhiteSpace(dto.DefaultLanguage) ? "en" : dto.DefaultLanguage,
            DefaultCurrency = string.IsNullOrWhiteSpace(dto.DefaultCurrency) ? "USD" : dto.DefaultCurrency,
            DefaultTimezone = string.IsNullOrWhiteSpace(dto.DefaultTimezone) ? "UTC" : dto.DefaultTimezone,
            Status = EntityStatus.Active
        };
        _db.Companies.Add(company);

        if (dto.PackageId != null)
        {
            var pkg = await _db.Packages.FirstOrDefaultAsync(p => p.Id == dto.PackageId && !p.IsDeleted);
            if (pkg == null) return BadRequest(ApiResponse.Fail("NOT_FOUND", "Package not found"));
            var sub = new Subscription
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                PackageId = pkg.Id,
                Status = SubscriptionStatus.Active,
                StartDate = DateTime.UtcNow,
                CurrentPrice = pkg.Price,
                Currency = pkg.Currency,
                BillingCycle = pkg.BillingCycle,
                TenantId = company.Id
            };
            _db.Subscriptions.Add(sub);
            company.SubscriptionId = sub.Id;
            company.PackageId = pkg.Id;
        }

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { company.Id, company.Name, company.Slug }));
    }

    // ── Delete Company ───────────────────────────────────
    [HttpDelete("companies/{id:guid}")]
    public async Task<ActionResult<ApiResponse>> DeleteCompany(Guid id)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
        if (company == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Company not found"));
        if (company.Slug == "platform")
            return BadRequest(ApiResponse.Fail("FORBIDDEN", "The platform company cannot be deleted"));

        var activeUsers = await _db.Users.CountAsync(u => u.CompanyId == id && !u.IsDeleted && u.Status == EntityStatus.Active);
        if (activeUsers > 0)
            return BadRequest(ApiResponse.Fail("HAS_USERS", $"Cannot delete this company: it still has {activeUsers} active user(s). Deactivate or move them first."));

        company.IsDeleted = true;
        company.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "Company deleted"));
    }

    // ── Locale validation (company may only pick from active master lists) ──
    private async Task<string?> ValidateCompanyLocaleAsync(string? language, string? currency)
    {
        if (!string.IsNullOrWhiteSpace(language) &&
            !await _db.Languages.AnyAsync(l => l.Code == language && !l.IsDeleted && l.Status == EntityStatus.Active))
            return $"Language '{language}' is not an active language. Choose from the platform's active language list.";
        if (!string.IsNullOrWhiteSpace(currency) &&
            !await _db.Currencies.AnyAsync(c => c.Code == currency && !c.IsDeleted && c.Status == EntityStatus.Active))
            return $"Currency '{currency}' is not an active currency. Choose from the platform's active currency list.";
        return null;
    }

    // ── Update Company (extended fields) ─────────────────
    [HttpPut("companies/{id:guid}/extended")]
    public async Task<ActionResult<ApiResponse>> UpdateCompanyExtended(Guid id, [FromBody] UpdateCompanyDto dto)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
        if (company == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Company not found"));

        var localeErr = await ValidateCompanyLocaleAsync(dto.DefaultLanguage, dto.DefaultCurrency);
        if (localeErr != null) return BadRequest(ApiResponse.Fail("INVALID_LOCALE", localeErr));

        if (dto.Name != null) company.Name = dto.Name;
        if (dto.Slug != null) company.Slug = dto.Slug;
        if (dto.LogoUrl != null) company.LogoUrl = dto.LogoUrl;
        if (dto.FaviconUrl != null) company.FaviconUrl = dto.FaviconUrl;
        if (dto.ContactEmail != null) company.ContactEmail = dto.ContactEmail;
        if (dto.ContactPhone != null) company.ContactPhone = dto.ContactPhone;
        if (dto.Website != null) company.Website = dto.Website;
        if (dto.Address != null) company.Address = dto.Address;
        if (dto.City != null) company.City = dto.City;
        if (dto.State != null) company.State = dto.State;
        if (dto.Country != null) company.Country = dto.Country;
        if (dto.PostalCode != null) company.PostalCode = dto.PostalCode;
        if (dto.DefaultLanguage != null) company.DefaultLanguage = dto.DefaultLanguage;
        if (dto.DefaultTimezone != null) company.DefaultTimezone = dto.DefaultTimezone;
        if (dto.DefaultCurrency != null) company.DefaultCurrency = dto.DefaultCurrency;
        if (dto.DateFormat != null) company.DateFormat = dto.DateFormat;
        if (dto.TimeFormat != null) company.TimeFormat = dto.TimeFormat;
        if (dto.NumberFormat != null) company.NumberFormat = dto.NumberFormat;
        if (dto.DefaultMapProvider.HasValue) company.DefaultMapProvider = (MapProvider)dto.DefaultMapProvider.Value;
        if (dto.MapApiKey != null) company.MapApiKey = dto.MapApiKey;
        if (dto.Status.HasValue) company.Status = (EntityStatus)dto.Status.Value;

        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "Company updated"));
    }

}
