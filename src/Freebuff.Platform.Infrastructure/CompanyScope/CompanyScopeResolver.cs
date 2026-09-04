using System.Security.Claims;
using System.Text.Json;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Shared.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Freebuff.Platform.Infrastructure.CompanyScope;

/// <summary>
/// Computes effective scope per request. The permitted-company set (NOT the
/// user's selected scope) is cached keyed by user id with a short TTL; scope
/// selection itself is stateless and re-derived from the request header on every
/// call. The cache only holds the active-company list for cross-tenant users —
/// a user whose cross-tenant role is revoked never reads it again, so staleness
/// is bounded by the TTL and can never widen a non-cross-tenant user's access.
/// </summary>
public sealed class CompanyScopeResolver : ICompanyScopeResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private const string KeyPrefix = "company-scope:user:";

    private readonly IDistributedCache _cache;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<CompanyScopeResolver> _logger;

    public CompanyScopeResolver(IDistributedCache cache, ApplicationDbContext db, ILogger<CompanyScopeResolver> logger)
    {
        _cache = cache;
        _db = db;
        _logger = logger;
    }

    public async Task<ResolvedCompanyScope> ResolveAsync(ClaimsPrincipal user, string? headerValue, CancellationToken ct = default)
    {
        if (user.Identity?.IsAuthenticated != true)
            return ResolvedCompanyScope.Unconstrained();

        var tenantId = user.FindFirst("tenant_id")?.Value is { } t && Guid.TryParse(t, out var tid) ? tid : (Guid?)null;
        var userIdRaw = user.GetUserIdString();
        var userId = Guid.TryParse(userIdRaw, out var uid) ? uid : (Guid?)null;
        var isCrossTenant = user.IsSuperAdmin();

        // ── Permitted company set ──
        IReadOnlyList<Guid> permitted;
        if (!isCrossTenant)
        {
            // Normal (non cross-tenant) users can only ever access their own company.
            permitted = tenantId is null ? Array.Empty<Guid>() : new[] { tenantId.Value };
        }
        else
        {
            permitted = await GetActiveCompanyIdsAsync(userId, ct);
        }

        // ── Requested scope (header expresses intent, never authorization) ──
        var requested = new List<Guid>();
        var headerTrim = headerValue?.Trim();
        if (!string.IsNullOrWhiteSpace(headerTrim)
            && !string.Equals(headerTrim, CompanyScopePolicy.All, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var part in headerTrim.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (Guid.TryParse(part, out var id)) requested.Add(id);
        }

        var wantsAll = string.IsNullOrWhiteSpace(headerTrim)
            || string.Equals(headerTrim, CompanyScopePolicy.All, StringComparison.OrdinalIgnoreCase);

        // ── Effective scope = requested ∩ permitted; drop the rest silently ──
        var dropped = new List<Guid>();
        List<Guid>? effective = null; // null = unconstrained (ALL)

        if (!isCrossTenant)
        {
            // Header can never widen access: effective scope is always own company.
            if (tenantId is not null)
                foreach (var id in requested)
                    if (id != tenantId.Value) dropped.Add(id);
            if (tenantId is not null) effective = new List<Guid> { tenantId.Value };
        }
        else if (!wantsAll)
        {
            effective = new List<Guid>();
            var permittedSet = permitted.ToHashSet();
            foreach (var id in requested)
            {
                if (permittedSet.Contains(id)) effective.Add(id);
                else dropped.Add(id);
            }
        }

        if (dropped.Count > 0)
        {
            _logger.LogWarning(
                "Company scope: user {UserId} requested company ids [{Requested}] outside permitted set; dropped [{Dropped}]. Client bug or probing attempt?",
                userIdRaw, string.Join(",", requested), string.Join(",", dropped));
        }

        return new ResolvedCompanyScope(tenantId, isCrossTenant, effective, dropped);
    }

    private async Task<IReadOnlyList<Guid>> GetActiveCompanyIdsAsync(Guid? userId, CancellationToken ct)
    {
        if (userId is null)
            return await _db.Companies.AsNoTracking()
                .Where(c => !c.IsDeleted && c.Status == EntityStatus.Active)
                .Select(c => c.Id).ToListAsync(ct);

        var key = $"{KeyPrefix}{userId}";
        var cached = await _cache.GetStringAsync(key, ct);
        if (cached is not null)
        {
            try
            {
                var ids = JsonSerializer.Deserialize<List<Guid>>(cached);
                if (ids is not null) return ids;
            }
            catch (JsonException)
            {
                // Corrupt entry — fall through and recompute.
            }
        }

        var fresh = await _db.Companies.AsNoTracking()
            .Where(c => !c.IsDeleted && c.Status == EntityStatus.Active)
            .Select(c => c.Id)
            .ToListAsync(ct);

        await _cache.SetStringAsync(key, JsonSerializer.Serialize(fresh),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl }, ct);
        return fresh;
    }
}
