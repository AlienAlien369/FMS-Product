namespace Freebuff.Platform.Application.DTOs;

public class CreateModuleDto
{
    /// <summary>Lowercase kebab-case slug, unique across modules.</summary>
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public int? Status { get; set; }
    public int? DisplayOrder { get; set; }
    public bool IsCore { get; set; }
}

public class UpdateModuleDto
{
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public int? Status { get; set; }
    public int? DisplayOrder { get; set; }
}

public class CreatePageDto
{
    /// <summary>Lowercase kebab-case slug. Doubles as the permission module code.</summary>
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Route { get; set; }
    public string? Icon { get; set; }
    public bool Nav { get; set; }
    public bool AdminOnly { get; set; }
    public bool Planned { get; set; } = true; // new pages are registered for the future by default
    public bool IsCore { get; set; }
    public int? Status { get; set; }
    public int? DisplayOrder { get; set; }
    public string? Description { get; set; }
}

public class UpdatePageDto
{
    public string? Key { get; set; }
    public string? Name { get; set; }
    public string? Route { get; set; }
    public string? Icon { get; set; }
    public bool? Nav { get; set; }
    public bool? AdminOnly { get; set; }
    public bool? Planned { get; set; }
    public int? Status { get; set; }
    public int? DisplayOrder { get; set; }
    public string? Description { get; set; }
}

public class ReorderModulesDto
{
    public List<Guid> ModuleIds { get; set; } = new();
}

public class ReorderPagesDto
{
    public Guid ModuleId { get; set; }
    public List<Guid> PageIds { get; set; } = new();
}