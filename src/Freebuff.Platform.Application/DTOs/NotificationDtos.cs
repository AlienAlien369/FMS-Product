namespace Freebuff.Platform.Application.DTOs;

public class NotificationDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? EventType { get; set; }
    public string? ActionUrl { get; set; }
    public int Severity { get; set; }
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UnreadCountDto
{
    public int Count { get; set; }
}

public class MarkReadDto
{
    public bool Success { get; set; }
}

// ── Notification Event Type registry (Super Admin) ────────────────────────

public class NotificationEventTypeDto
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

public class CreateNotificationEventTypeDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public int DefaultSeverity { get; set; } = 2;
    public int DisplayOrder { get; set; }
}

public class UpdateNotificationEventTypeDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public int? DefaultSeverity { get; set; }
    public int? DisplayOrder { get; set; }
    public int? Status { get; set; }
}

// ── Per-user preferences ──────────────────────────────────────────────────

public class NotificationPreferenceDto
{
    public string EventType { get; set; } = string.Empty;
    public string EventTypeName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public class UpdateNotificationPreferenceDto
{
    public string EventType { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}