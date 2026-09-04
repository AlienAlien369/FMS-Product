using System.Reflection;
using Freebuff.Platform.Ingestion.Contracts;

namespace Freebuff.Platform.Ingestion.Registry;

/// <summary>
/// Resolves a vendor code (DeviceVendor.Code) to its IVendorAdapter. Built from
/// attribute-scanned types — adding a vendor = one new class, no other changes.
/// Resolution is fail-safe: unknown codes return null, never throw.
/// </summary>
public sealed class VendorAdapterRegistry : IVendorAdapterRegistry
{
    private readonly IReadOnlyDictionary<string, IVendorAdapter> _byCode;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<IVendorAdapter>> _byTransport;

    private VendorAdapterRegistry(IEnumerable<IVendorAdapter> adapters)
    {
        var list = adapters.ToList();
        _byCode = list.ToDictionary(a => a.VendorCode, StringComparer.OrdinalIgnoreCase);
        _byTransport = list
            .GroupBy(a => a.ProtocolType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<IVendorAdapter>)g.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Scans the assembly containing the given marker type for [VendorAdapter] implementations.</summary>
    public static VendorAdapterRegistry Scan(Assembly assembly)
    {
        var adapters = assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IVendorAdapter).IsAssignableFrom(t))
            .Select(t => (t, attr: t.GetCustomAttribute<VendorAdapterAttribute>()))
            .Where(x => x.attr != null)
            .Select(x => (IVendorAdapter)Activator.CreateInstance(x.t)!)
            .ToList();

        return new VendorAdapterRegistry(adapters);
    }

    /// <summary>Registry containing every built-in adapter in this assembly.</summary>
    public static VendorAdapterRegistry CreateBuiltIn() => Scan(typeof(VendorAdapterRegistry).Assembly);

    public IVendorAdapter? Get(string vendorCode)
        => string.IsNullOrEmpty(vendorCode) ? null : _byCode.GetValueOrDefault(vendorCode);

    public IReadOnlyList<IVendorAdapter> ForTransport(string protocolType)
        => _byTransport.GetValueOrDefault(protocolType) ?? Array.Empty<IVendorAdapter>();

    public IEnumerable<IVendorAdapter> All => _byCode.Values;
}
