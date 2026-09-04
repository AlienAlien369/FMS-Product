namespace Freebuff.Platform.Ingestion.Contracts;

/// <summary>
/// Transport/framing layer — turns an incremental byte stream into complete
/// frames. One codec is shared by every vendor that speaks the same protocol
/// family (e.g. JT808-style framing), keeping transport and payload parsing as
/// two independent axes. A vendor with an existing transport needs no codec.
/// </summary>
public interface ITransportCodec
{
    string ProtocolType { get; } // tcp | udp | http | mqtt

    /// <summary>Feeds a chunk and yields complete frames; incomplete data is buffered per-connection by the caller via context.</summary>
    IEnumerable<byte[]> Frame(byte[] chunk, ref FrameContext context);
}

/// <summary>Per-connection framing state (carry over partial data between chunks).</summary>
public sealed class FrameContext
{
    public byte[] Pending { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// Default codec for single-frame transports (HTTP webhook, JSON-over-MQTT):
/// each delivery is one complete frame; nothing to buffer.
/// </summary>
public sealed class NoFramingCodec : ITransportCodec
{
    public string ProtocolType { get; init; } = "http";
    public IEnumerable<byte[]> Frame(byte[] chunk, ref FrameContext context) => new[] { chunk };
}
