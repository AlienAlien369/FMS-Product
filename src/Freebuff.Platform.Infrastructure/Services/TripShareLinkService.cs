using System.Security.Cryptography;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>
/// Customer Tracking Link share tokens. The access rule is deliberately narrow
/// and outside RBAC: a valid, unexpired, non-revoked token resolves to exactly
/// one trip — nothing else. Revocation is a DB-backed check on every request so
/// a revoked link dies immediately (no caching grace period).
///
/// Token space: 32 random bytes (256 bits) URL-safe base64-encoded (43 chars) —
/// guessing is computationally infeasible; the unique index makes collisions
/// impossible rather than merely improbable.
/// </summary>
public class TripShareLinkService
{
    /// <summary>Default link lifetime: trip completion + N days (configurable constant today).</summary>
    public const int DefaultLinkLifetimeDays = 30;

    private readonly ApplicationDbContext _db;

    public TripShareLinkService(ApplicationDbContext db) => _db = db;

    /// <summary>Creates a share link; expiry = explicit days, else (trip completion ?? now) + DefaultLinkLifetimeDays.</summary>
    public async Task<TripShareLink> GenerateAsync(Guid tripId, string createdBy, int? expiresInDays = null)
    {
        var trip = await _db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripId && !t.IsDeleted)
            ?? throw new KeyNotFoundException("Trip not found.");

        var expiresAt = expiresInDays.HasValue
            ? DateTime.UtcNow.AddDays(expiresInDays.Value)
            : (trip.ActualEndTime ?? DateTime.UtcNow).AddDays(DefaultLinkLifetimeDays);

        var link = new TripShareLink
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            CompanyId = trip.CompanyId,
            TenantId = trip.CompanyId,
            Token = CreateToken(),
            ExpiresAt = expiresAt,
            CreatedByUserId = createdBy,
        };
        _db.TripShareLinks.Add(link);
        await _db.SaveChangesAsync();
        return link;
    }

    /// <summary>256-bit URL-safe token (no padding, no +/=). Static for direct testing.</summary>
    public static string CreateToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /// <summary>Active (non-revoked, non-expired) links for a trip — the internal audit list.</summary>
    public Task<List<TripShareLink>> ListForTripAsync(Guid tripId)
        => _db.TripShareLinks.AsNoTracking()
            .Where(l => l.TripId == tripId && !l.IsDeleted && !l.IsRevoked
                && (l.ExpiresAt == null || l.ExpiresAt > DateTime.UtcNow))
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();

    /// <summary>Revokes a link immediately; returns false when the link is unknown.</summary>
    public async Task<bool> RevokeAsync(Guid tripId, Guid linkId, string revokedBy)
    {
        var link = await _db.TripShareLinks.FirstOrDefaultAsync(l => l.Id == linkId && l.TripId == tripId && !l.IsDeleted);
        if (link == null) return false;
        link.IsRevoked = true;
        link.UpdatedAt = DateTime.UtcNow;
        link.UpdatedBy = revokedBy;
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// The ONLY public validation: token possession + not revoked + not expired +
    /// trip not deleted. Returns the link and trip, or null for any failure — the
    /// public surface maps null to one clean "no longer available" state so
    /// unknown, revoked and expired tokens are indistinguishable.
    /// </summary>
    public async Task<(TripShareLink Link, Trip Trip)?> ResolveValidAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var link = await _db.TripShareLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Token == token && !l.IsDeleted && !l.IsRevoked
                && (l.ExpiresAt == null || l.ExpiresAt > DateTime.UtcNow));
        if (link == null) return null;
        var trip = await _db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.Id == link.TripId && !t.IsDeleted);
        if (trip == null) return null;
        return (link, trip);
    }
}