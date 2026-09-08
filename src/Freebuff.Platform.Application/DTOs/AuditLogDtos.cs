using System.Text.Json;

namespace Freebuff.Platform.Application.DTOs;

/// <summary>
/// One audit entry as served to the Audit Log UI. beforeState/afterState are
/// embedded JSON (permission diffs, package changes, scope selections) — never
/// raw strings, so the frontend can render them without re-parsing.
/// </summary>
public class AuditLogEntryDto
{
    public Guid Id { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? ActorEmail { get; set; }
    public string? ActorRole { get; set; }
    public string ActionCode { get; set; } = "";
    public string Action { get; set; } = "";
    public string TargetEntityType { get; set; } = "";
    public Guid? TargetEntityId { get; set; }
    public string? TargetEntityName { get; set; }
    public Guid? TargetCompanyId { get; set; }
    public JsonElement? BeforeState { get; set; }
    public JsonElement? AfterState { get; set; }
    public string? IpAddress { get; set; }
    public string? Source { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>Body for POST /api/v1/audit/scope-switch — which companies a SuperAdmin switched into.</summary>
public class ScopeSwitchDto
{
    public List<Guid> CompanyIds { get; set; } = new();
}