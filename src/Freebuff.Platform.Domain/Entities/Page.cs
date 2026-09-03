using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// A page/form inside a top-level Module. Rows are seeded from the canonical
/// PageRegistry at startup (create-missing only, so SuperAdmin edits survive)
/// and are fully manageable by SuperAdmin through the admin API: status toggles,
/// reorder, planned flags, and for non-core pages rename/delete.
/// </summary>
public class Page : BaseEntity
{
    /// <summary>The top-level module this page belongs to (one owner per page).</summary>
    public Guid ModuleId { get; set; }
    public Module? Module { get; set; }

    /// <summary>Stable slug. Doubles as the permission module code ("vehicle" → vehicle.view). Globally unique (permission codes derive from it).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Canonical display name (nav label + permission group label).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Frontend route. Null = no standalone page yet (planned/tab feature).</summary>
    public string? Route { get; set; }

    public string? Icon { get; set; }

    /// <summary>Appears in the sidebar.</summary>
    public bool Nav { get; set; }

    /// <summary>SuperAdmin-only page.</summary>
    public bool AdminOnly { get; set; }

    /// <summary>Registered for a future page — grants no nav/route access today.</summary>
    public bool Planned { get; set; }

    /// <summary>System page (e.g. Dashboard, Platform Admin): cannot be deleted or renamed — status toggle only.</summary>
    public bool IsCore { get; set; }

    public EntityStatus Status { get; set; } = EntityStatus.Active;

    public int DisplayOrder { get; set; }

    public string? Description { get; set; }
}