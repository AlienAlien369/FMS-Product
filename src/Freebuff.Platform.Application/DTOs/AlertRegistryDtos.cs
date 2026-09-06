namespace Freebuff.Platform.Application.DTOs;

// ── Platform Catalog (Super Admin) ─────────────────────────────────────────

public class AlertTypeDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public int DefaultSeverity { get; set; }
    public int DisplayOrder { get; set; }
    public int Status { get; set; }
}

public class CreateAlertTypeDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public int DefaultSeverity { get; set; } = 2;
    public int DisplayOrder { get; set; }
}

public class UpdateAlertTypeDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public int? DefaultSeverity { get; set; }
    public int? DisplayOrder { get; set; }
    public int? Status { get; set; }
}

// ── Company Alert Subscription (Super Admin per-company) ───────────────────

public class CompanyAlertSubscriptionDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public Guid AlertTypeId { get; set; }
    public string AlertTypeCode { get; set; } = string.Empty;
    public string AlertTypeName { get; set; } = string.Empty;
    public string AlertTypeCategory { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string Source { get; set; } = string.Empty;
}

public class ToggleCompanyAlertDto
{
    public bool Enabled { get; set; }
}

// ── Role Alert Visibility (Company Admin) ──────────────────────────────────

public class RoleAlertVisibilityDto
{
    public Guid Id { get; set; }
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public Guid AlertTypeId { get; set; }
    public string AlertTypeCode { get; set; } = string.Empty;
    public string AlertTypeName { get; set; } = string.Empty;
    public string AlertTypeCategory { get; set; } = string.Empty;
    public bool Visible { get; set; }
}

public class ToggleRoleAlertVisibilityDto
{
    public bool Visible { get; set; }
}

// ── Bulk operations ────────────────────────────────────────────────────────

public class BulkToggleCompanyAlertsDto
{
    public bool Enabled { get; set; }
    public string? Category { get; set; } // null = all
}

public class BulkToggleRoleAlertsDto
{
    public bool Visible { get; set; }
    public string? Category { get; set; } // null = all
}
