namespace Freebuff.Platform.Ingestion.Contracts;

/// <summary>
/// Marks an IVendorAdapter implementation for auto-registration. The registry
/// scans its assembly for decorated types; VendorCode must equal DeviceVendor.Code.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class VendorAdapterAttribute : Attribute
{
    public string VendorCode { get; }
    public VendorAdapterAttribute(string vendorCode) => VendorCode = vendorCode;
}
