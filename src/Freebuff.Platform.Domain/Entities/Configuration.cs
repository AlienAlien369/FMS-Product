using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Central configuration entity supporting the configuration hierarchy:
/// System → Module → Package → Company → Role → User
/// </summary>
public class Configuration : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public ConfigurationValueType ValueType { get; set; } = ConfigurationValueType.String;
    public ConfigurationScope Scope { get; set; } = ConfigurationScope.System;
    public string? Module { get; set; }
    public Guid? ScopeEntityId { get; set; } // RoleId, UserId, PackageId, etc.
    public string? Description { get; set; }
    public string? DefaultValue { get; set; }
    public bool IsEditable { get; set; } = true;
    public bool IsVisible { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string? ValidationRules { get; set; } // JSON schema for validation
    public EntityStatus Status { get; set; } = EntityStatus.Active;

    // Navigation
    public Guid? CompanyId { get; set; }
}
