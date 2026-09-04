using Freebuff.Platform.Ingestion.Contracts;

namespace Freebuff.Platform.Ingestion.Adapters;

/// <summary>
/// PLACEHOLDER — Pictor TCP adapter. Pictor's binary/TCP protocol specification
/// and a sample feed are not available yet, so this adapter exists only to prove
/// the plug-in point: it is registered, listed in the catalog, and fails
/// GRACEFULLY (never throws, never crashes the pipeline for other vendors) until
/// the real protocol is implemented by replacing this class body.
/// </summary>
[VendorAdapter("pictor")]
public sealed class PictorTcpPlaceholderAdapter : IVendorAdapter
{
    public string VendorCode => "pictor";
    public string ProtocolType => "tcp";
    public string PayloadFormat => "pictor-binary-v2";
    public string PlaceholderNote => "PLACEHOLDER — protocol spec not yet available; replace this body with the real Pictor parse.";

    public bool TryExtractDeviceId(byte[] frame, out DeviceIdentity identity)
    {
        identity = DeviceIdentity.None;
        return false; // cannot identify frames until the real spec lands
    }

    public bool Validate(byte[] frame, out string? error)
    {
        error = PlaceholderNote;
        return false;
    }

    public ParseResult Parse(byte[] frame, DateTime receivedAtUtc)
        => new ParseRejected(PlaceholderNote);
}

/// <summary>
/// PLACEHOLDER — iTriangle adapter. Same story as <see cref="PictorTcpPlaceholderAdapter"/>:
/// registered so the vendor row exists and the registry stays complete, rejecting
/// all traffic gracefully until the real payload format is documented.
/// </summary>
[VendorAdapter("itriangle")]
public sealed class ItriangleHttpPlaceholderAdapter : IVendorAdapter
{
    public string VendorCode => "itriangle";
    public string ProtocolType => "http";
    public string PayloadFormat => "itriangle-json-v1";
    public string PlaceholderNote => "PLACEHOLDER — protocol spec not yet available; replace this body with the real iTriangle parse.";

    public bool TryExtractDeviceId(byte[] frame, out DeviceIdentity identity)
    {
        identity = DeviceIdentity.None;
        return false;
    }

    public bool Validate(byte[] frame, out string? error)
    {
        error = PlaceholderNote;
        return false;
    }

    public ParseResult Parse(byte[] frame, DateTime receivedAtUtc)
        => new ParseRejected(PlaceholderNote);
}
