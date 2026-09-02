namespace Freebuff.Platform.Application.DTOs;

public class CompanyDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? LogoUrl { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Country { get; set; }
    public string DefaultLanguage { get; set; } = "en";
    public string DefaultTimezone { get; set; } = "UTC";
    public string DefaultCurrency { get; set; } = "USD";
    public int Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public int UserCount { get; set; }
    public int VehicleCount { get; set; }
}

public class CreateCompanyDto
{
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Website { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? PostalCode { get; set; }
    public string DefaultLanguage { get; set; } = "en";
    public string DefaultTimezone { get; set; } = "UTC";
    public string DefaultCurrency { get; set; } = "USD";
    public Guid? PackageId { get; set; }
}

public class UpdateCompanyDto
{
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Website { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? PostalCode { get; set; }
    public string? DefaultLanguage { get; set; }
    public string? DefaultTimezone { get; set; }
    public string? DefaultCurrency { get; set; }
    public string? DateFormat { get; set; }
    public string? TimeFormat { get; set; }
    public string? NumberFormat { get; set; }
    public int? DefaultMapProvider { get; set; }
    public string? MapApiKey { get; set; }
    public int? Status { get; set; }
}

public class CreateUserDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public Guid CompanyId { get; set; }
    public List<Guid>? RoleIds { get; set; }
}

public class UpdateUserDto
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Language { get; set; }
    public string? Timezone { get; set; }
    public string? Currency { get; set; }
    public int? Status { get; set; }
    public List<Guid>? RoleIds { get; set; }
}

public class RoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid CompanyId { get; set; }
    public int Status { get; set; }
    public bool IsSystemRole { get; set; }
    public List<PermissionDto> Permissions { get; set; } = new();
    public int UserCount { get; set; }
}

public class CreateRoleDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid CompanyId { get; set; }
    public List<Guid>? PermissionIds { get; set; }
}

public class UpdateRoleDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? Status { get; set; }
    public List<Guid>? PermissionIds { get; set; }
}

public class PermissionDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Module { get; set; } = string.Empty;
    public int Action { get; set; }
}

public class AuditEntryDto
{
    public Guid Id { get; set; }
    public int Action { get; set; }
    public int EntityType { get; set; }
    public Guid EntityId { get; set; }
    public string? EntityName { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime CreatedAt { get; set; }
}
