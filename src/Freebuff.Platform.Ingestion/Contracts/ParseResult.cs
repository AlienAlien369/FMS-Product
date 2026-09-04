namespace Freebuff.Platform.Ingestion.Contracts;

public abstract record ParseResult;

/// <summary>Frame parsed into the normalized schema.</summary>
public sealed record ParseOk(NormalizedTelemetry Telemetry) : ParseResult;

/// <summary>Frame was structurally invalid for this vendor. Never throws — the pipeline logs and dead-letters.</summary>
public sealed record ParseRejected(string Reason) : ParseResult;

/// <summary>Frame is incomplete for a streamed/segmented protocol; wait for more data.</summary>
public sealed record NeedsMoreData() : ParseResult;
