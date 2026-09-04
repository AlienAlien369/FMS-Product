namespace Freebuff.Platform.Ingestion.Contracts;

/// <summary>
/// Identity extracted from a raw payload frame. Vendor code must match a
/// DeviceVendor.Code and the device lookup key (identity value) must match a
/// registered Device.IdentityValue of the same identity type.
/// </summary>
public readonly record struct DeviceIdentity(string VendorCode, string IdentityType, string IdentityValue)
{
    public static readonly DeviceIdentity None = new(string.Empty, string.Empty, string.Empty);
    public bool IsEmpty => string.IsNullOrEmpty(VendorCode);
}
