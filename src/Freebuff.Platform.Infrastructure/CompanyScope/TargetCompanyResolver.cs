using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.CompanyScope;

/// <summary>
/// Resolves the single company a create/update write targets, and audits
/// SuperAdmin cross-company writes.
///
/// Security boundary: the X-Company-Scope header is only ever a *view*
/// preference — it never decides where a write lands. Every write resolves its
/// target company here:
///  - Non-cross-tenant callers are always forced to their own tenant; any
///    client-supplied companyId is ignored.
///  - SuperAdmin (cross-tenant) must supply an explicit companyId that
///    references an existing, non-deleted, active company; otherwise the write
///    is rejected. The target company — not the view scope — controls the write.
/// </summary>
public class TargetCompanyResolver
{
    private readonly ITenantContext _tenant;
    private readonly ApplicationDbContext _db;

    public TargetCompanyResolver(ITenantContext tenant, ApplicationDbContext db)
    {
        _tenant = tenant;
        _db = db;
    }

    /// <summary>
    /// Returns the company the current write must be attributed to.
    /// Throws when a cross-tenant caller omits companyId or names a company
    /// that does not exist / is not active.
    /// </summary>
    public async Task<Guid> ResolveAsync(Guid? requested)
    {
        if (_tenant.IsSuperAdmin)
        {
            if (requested == null || requested == Guid.Empty)
                throw new ArgumentException("CompanyId is required for this write — select the target company.");
            var exists = await _db.Companies.AsNoTracking()
                .AnyAsync(c => c.Id == requested.Value && !c.IsDeleted && c.Status == EntityStatus.Active);
            if (!exists)
                throw new ArgumentException("Target company does not exist or is inactive.");
            return requested.Value;
        }
        return _tenant.TenantId ?? throw new UnauthorizedAccessException("No tenant context");
    }

    /// <summary>True when a SuperAdmin writes into a company other than their own.</summary>
    public bool IsCrossTenantWrite(Guid targetCompanyId) =>
        _tenant.IsSuperAdmin && _tenant.TenantId.HasValue && targetCompanyId != _tenant.TenantId.Value;

    /// <summary>
    /// Records a SuperAdmin cross-company write (who, which company, what
    /// record, when). In-tenant writes are skipped — those flows keep their own
    /// existing audit behavior.
    /// </summary>
    public void Audit(AuditAction action, EntityType entityType, Guid entityId, string? entityName, string? newValues, Guid targetCompanyId)
    {
        if (!IsCrossTenantWrite(targetCompanyId)) return;
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = targetCompanyId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _tenant.UserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            EntityName = entityName,
            NewValues = newValues,
            UserId = Guid.TryParse(_tenant.UserId, out var uid) ? uid : Guid.Empty,
            UserName = _tenant.UserRole,
            Source = "SuperAdmin cross-tenant write"
        });
    }
}