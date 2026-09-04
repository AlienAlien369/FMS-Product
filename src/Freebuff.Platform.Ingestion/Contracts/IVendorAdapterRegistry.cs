namespace Freebuff.Platform.Ingestion.Contracts;

/// <summary>
/// Resolves a vendor code (DeviceVendor.Code) to its IVendorAdapter. Fail-safe:
/// unknown codes return null, never throw.
/// </summary>
public interface IVendorAdapterRegistry
{
    IVendorAdapter? Get(string vendorCode);
    IReadOnlyList<IVendorAdapter> ForTransport(string protocolType);
    IEnumerable<IVendorAdapter> All { get; }
}
