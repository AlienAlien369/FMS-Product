using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Platform alert catalog — the master list of all alert types the platform can
/// generate. Managed by Super Admin only. Each entry is a registry row (like
/// PageRegistry), not a hardcoded enum scattered through the codebase.
/// </summary>
public class AlertType : BaseEntity
{
    /// <summary>Unique machine-readable code, e.g. "geofence.entry".</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description of when this alert fires.</summary>
    public string? Description { get; set; }

    /// <summary>Category grouping: Geofence, Route, Trip, Vehicle, Driver, Device, System.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Default severity when this alert fires (overridable per-alert-instance).</summary>
    public int DefaultSeverity { get; set; } = 2; // AlertSeverity.Medium

    /// <summary>Display order within category.</summary>
    public int DisplayOrder { get; set; }

    public EntityStatus Status { get; set; } = EntityStatus.Active;
}

/// <summary>
/// Per-company alert entitlement — which alert types a company is allowed to
/// receive. Derived from the company's package by default, with optional
/// per-company override by Super Admin.
/// </summary>
public class CompanyAlertSubscription : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid AlertTypeId { get; set; }
    public AlertType AlertType { get; set; } = null!;

    /// <summary>Whether this company is entitled to receive this alert type.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How this entitlement was determined:
    /// "package_default" — derived from the company's package tier.
    /// "admin_override"  — Super Admin explicitly enabled/disabled this per-company.
    /// </summary>
    public string Source { get; set; } = "package_default";
}

/// <summary>
/// Per-role alert visibility — which of the company's entitled alert types
/// each role can actually see in the UI/notifications. Managed by Company Admin.
/// Only alert types where CompanyAlertSubscription.Enabled = true can be made visible.
/// </summary>
public class RoleAlertVisibility : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public Guid AlertTypeId { get; set; }
    public AlertType AlertType { get; set; } = null!;

    /// <summary>Whether this role can see this alert type in the UI.</summary>
    public bool Visible { get; set; } = true;
}
