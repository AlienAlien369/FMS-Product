namespace Freebuff.Platform.Ingestion.Contracts;

/// <summary>
/// Payload-parsing layer of the vendor adapter. Transport is a separate axis
/// (<see cref="ITransportCodec"/>), so a vendor's protocol and payload format are
/// independent. Adapters are stateless and registered into the registry by their
/// <see cref="VendorCode"/> — adding a vendor never touches ingestion/business logic.
/// </summary>
public interface IVendorAdapter
{
    /// <summary>Stable vendor key — must match DeviceVendor.Code (e.g. "sample-json", "pictor").</summary>
    string VendorCode { get; }

    /// <summary>Transport the vendor uses: tcp | udp | http | mqtt (string axis of ITransportCodec).</summary>
    string ProtocolType { get; }

    /// <summary>Human/version label of the payload format, e.g. "json-webhook", "pictor-binary-v2".</summary>
    string PayloadFormat { get; }

    /// <summary>
    /// Pull a device identity out of a complete frame. Used for auto-detection
    /// and for the vendor-suggestion helper at device registration. Returns
    /// false when the frame doesn't carry an identity for this vendor.
    /// </summary>
    bool TryExtractDeviceId(byte[] frame, out DeviceIdentity identity);

    /// <summary>Cheap structural validation before a full parse. Never throws.</summary>
    bool Validate(byte[] frame, out string? error);

    /// <summary>Translate a complete frame into the normalized schema. Never throws — return ParseRejected on garbage.</summary>
    ParseResult Parse(byte[] frame, DateTime receivedAtUtc);
}
