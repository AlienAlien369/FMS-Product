using System.Threading.Channels;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>
/// A fully-materialized audit entry ready for the background writer. The record
/// is complete at enqueue time — the writer never needs request-scoped state
/// (tenant context, user, IP) because the caller captured it.
/// </summary>
public sealed class AuditLogRecord
{
    public Guid ActorUserId { get; init; }
    public string? ActorRole { get; init; }
    public string? ActorEmail { get; init; }
    public AuditAction Action { get; init; }
    public string? ActionCode { get; init; }
    public EntityType EntityType { get; init; }
    public Guid EntityId { get; init; }
    public string? EntityName { get; init; }
    public Guid? TargetCompanyId { get; init; }
    public string? BeforeState { get; init; }
    public string? AfterState { get; init; }
    public string? IpAddress { get; init; }
    public string? Source { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Central owner of audit entries — the ONLY place an AuditLog row is created.
///
/// Non-blocking by contract: <see cref="TryRecord"/> enqueues to a bounded
/// channel and returns immediately, so a slow or unavailable audit write can
/// never fail or delay the user-facing operation it describes. A full or
/// stopped channel drops the write (never throws). A background host drains
/// the channel and INSERTs rows.
///
/// Append-only by construction: there is no update/delete path on the entity
/// anywhere in the platform, and the read surface is a separate query-only
/// controller. Enforcing this at the DB layer (runtime role granted only
/// INSERT/SELECT) is a deployment concern — the application guarantees it.
///
/// Retention: entries keep full detail (JSON before/after) for
/// <see cref="RetentionMonths"/> (12). Beyond that a future rollup job
/// summarizes to hourly/daily; the schema is already shaped for it (the
/// before/after JSON is self-describing and non-null columns stay stable).
/// </summary>
public sealed class AuditLogService
{
    /// <summary>Full-detail retention window before archival/summarization is considered.</summary>
    public const int RetentionMonths = 12;

    private readonly Channel<AuditLogRecord> _channel = Channel.CreateBounded<AuditLogRecord>(
        new BoundedChannelOptions(4096)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    /// <summary>Enqueue an entry. Never throws, never blocks on the caller.</summary>
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(ILogger<AuditLogService> logger)
    {
        _logger = logger;
    }

    public bool TryRecord(AuditLogRecord record)
    {
        try
        {
            var ok = _channel.Writer.TryWrite(record);
            if (!ok) _logger.LogWarning("Audit channel full — entry dropped ({Code})", record.ActionCode);
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit enqueue failed ({Code})", record.ActionCode);
            return false;
        }
    }

    public IAsyncEnumerable<AuditLogRecord> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);

    /// <summary>Try to dequeue one more already-buffered record (writer batching).</summary>
    public bool TryReadOne(out AuditLogRecord? record)
        => _channel.Reader.TryRead(out record);
}

/// <summary>
/// Drains the audit channel and INSERTs entries with a scoped DbContext.
/// A failed insert is logged and skipped — it must not crash the host or stop
/// subsequent entries (the operation being audited already succeeded).
/// </summary>
public sealed class AuditLogWriterHostedService : BackgroundService
{
    private const int BatchSize = 50;
    private readonly AuditLogService _service;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditLogWriterHostedService> _logger;

    public AuditLogWriterHostedService(
        AuditLogService service,
        IServiceScopeFactory scopeFactory,
        ILogger<AuditLogWriterHostedService> logger)
    {
        _service = service;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var first in _service.ReadAllAsync(stoppingToken))
        {
            var batch = new List<AuditLogRecord> { first };
            // Drain whatever else is already queued (never wait — latency matters more than batch size).
            while (batch.Count < BatchSize && _service.TryReadOne(out var more))
                batch.Add(more);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                foreach (var r in batch)
                    db.AuditLogs.Add(Map(r));
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audit writer failed to persist {Count} entries — skipped", batch.Count);
            }
        }
    }

    private static AuditLog Map(AuditLogRecord r) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = r.TargetCompanyId,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = r.ActorUserId.ToString(),
        UserId = r.ActorUserId,
        UserName = r.ActorEmail,
        ActorRole = r.ActorRole,
        Action = r.Action,
        ActionCode = r.ActionCode,
        EntityType = r.EntityType,
        EntityId = r.EntityId,
        EntityName = r.EntityName,
        OldValues = r.BeforeState,
        NewValues = r.AfterState,
        IpAddress = r.IpAddress,
        Source = r.Source,
        Reason = r.Reason
    };
}