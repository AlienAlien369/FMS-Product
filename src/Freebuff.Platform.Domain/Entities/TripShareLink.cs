using Freebuff.Platform.Domain.Common;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// A share token for the public Customer Tracking Link — the first access
/// primitive that is NOT a user, role, or company member. Possession of the
/// cryptographically random Token alone grants read-only access to exactly one
/// trip (magic-link semantics); it intentionally bypasses the RBAC/permission
/// registry so it can never inherit broader access through a role mapping.
///
/// Unguessable (256-bit URL-safe token), revocable (IsRevoked is re-checked on
/// every request — no caching grace period), time-bounded (ExpiresAt), and
/// single-purpose (scoped to TripId). Tenant isolation for the MANAGEMENT UI
/// mirrors the parent trip; the public surface validates by token only.
/// </summary>
public class TripShareLink : BaseEntity
{
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;

    public Guid CompanyId { get; set; }

    /// <summary>URL-safe unguessable token (43 chars, 256 bits) — the access credential.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>UTC bound; null = never expires. Default = trip completion + N days.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Revoked links are dead on the next request (DB-backed check, no cache).</summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// Which internal user generated the link (audit list). A dedicated property,
    /// NOT BaseEntity.CreatedBy — the DbContext's audit convention overwrites
    /// CreatedBy with the ambient request user.
    /// </summary>
    public string CreatedByUserId { get; set; } = string.Empty;
}