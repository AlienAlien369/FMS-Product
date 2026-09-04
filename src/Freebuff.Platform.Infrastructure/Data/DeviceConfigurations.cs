using Freebuff.Platform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Freebuff.Platform.Infrastructure.Data;

/// <summary>
/// EF configurations for the Device Abstraction Layer. Applied automatically to
/// ApplicationDbContext via ApplyConfigurationsFromAssembly. FleetDbContext (a
/// different assembly) mirrors the same shapes inline so the aspirational fleet
/// microservice cannot drift structurally.
/// </summary>
public class DeviceVendorConfiguration : IEntityTypeConfiguration<DeviceVendor>
{
    public void Configure(EntityTypeBuilder<DeviceVendor> b)
    {
        b.ToTable("DeviceVendors");
        b.HasIndex(v => v.Code).IsUnique().HasDatabaseName("IX_DeviceVendors_Code").HasFilter("\"IsDeleted\" = false");
        b.Property(v => v.Code).HasMaxLength(50).IsRequired();
        b.Property(v => v.Name).HasMaxLength(200).IsRequired();
        b.HasQueryFilter(v => !v.IsDeleted);
    }
}

public class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> b)
    {
        b.ToTable("Devices");
        b.HasIndex(d => new { d.CompanyId, d.IdentityType, d.IdentityValue })
            .IsUnique()
            .HasDatabaseName("UX_Devices_Company_Identity")
            .HasFilter("\"IsDeleted\" = false");
        b.HasIndex(d => d.VendorId).HasDatabaseName("IX_Devices_VendorId");
        b.Property(d => d.IdentityValue).HasMaxLength(100).IsRequired();
        b.Property(d => d.IdentityType).HasConversion<int>();
        b.Property(d => d.DeviceType).HasConversion<int>();
        b.Property(d => d.Status).HasConversion<int>();
        b.HasOne<DeviceVendor>().WithMany().HasForeignKey(d => d.VendorId).OnDelete(DeleteBehavior.Restrict);
        b.HasQueryFilter(d => !d.IsDeleted);
    }
}

public class DeviceSimConfiguration : IEntityTypeConfiguration<DeviceSim>
{
    public void Configure(EntityTypeBuilder<DeviceSim> b)
    {
        b.ToTable("DeviceSims");
        b.HasIndex(s => s.DeviceId).HasDatabaseName("IX_DeviceSims_DeviceId");
        // One active primary SIM per device.
        b.HasIndex(s => s.DeviceId).IsUnique().HasDatabaseName("UX_DeviceSims_ActivePrimary").HasFilter("\"IsPrimary\" = true AND \"IsDeleted\" = false");
        b.Property(s => s.Status).HasConversion<int>();
        b.HasOne<Device>().WithMany().HasForeignKey(s => s.DeviceId).OnDelete(DeleteBehavior.Cascade);
        b.HasQueryFilter(s => !s.IsDeleted);
    }
}

public class VehicleDeviceConfiguration : IEntityTypeConfiguration<VehicleDevice>
{
    public void Configure(EntityTypeBuilder<VehicleDevice> b)
    {
        b.ToTable("VehicleDevices");
        b.HasIndex(vd => vd.VehicleId).HasDatabaseName("IX_VehicleDevices_VehicleId");
        b.HasIndex(vd => vd.DeviceId).HasDatabaseName("IX_VehicleDevices_DeviceId");
        // One active assignment per (vehicle, role) — a vehicle can host many
        // devices (primary tracker + dashcam + fuel sensor) but only one of each role.
        b.HasIndex(vd => new { vd.VehicleId, vd.Role })
            .IsUnique()
            .HasDatabaseName("UX_VehicleDevices_Vehicle_Role_Active")
            .HasFilter("\"AssignedTo\" IS NULL AND \"IsDeleted\" = false");
        b.Property(vd => vd.Role).HasConversion<int>();
        b.HasOne<Vehicle>().WithMany().HasForeignKey(vd => vd.VehicleId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Device>().WithMany().HasForeignKey(vd => vd.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.HasQueryFilter(vd => !vd.IsDeleted);
    }
}

public class TelemetryEventConfiguration : IEntityTypeConfiguration<TelemetryEvent>
{
    public void Configure(EntityTypeBuilder<TelemetryEvent> b)
    {
        b.ToTable("TelemetryEvents");
        b.HasIndex(e => e.DeviceId).HasDatabaseName("IX_TelemetryEvents_DeviceId");
        b.HasIndex(e => new { e.VehicleId, e.EventTimeUtc }).HasDatabaseName("IX_TelemetryEvents_Vehicle_Time");
    }
}

public class TelemetryStateConfiguration : IEntityTypeConfiguration<TelemetryState>
{
    public void Configure(EntityTypeBuilder<TelemetryState> b)
    {
        b.ToTable("TelemetryStates");
        b.HasIndex(s => s.VehicleId).IsUnique().HasDatabaseName("UX_TelemetryStates_VehicleId");
        b.HasIndex(s => s.DeviceId).HasDatabaseName("IX_TelemetryStates_DeviceId");
    }
}

public class RawPayloadConfiguration : IEntityTypeConfiguration<RawPayload>
{
    public void Configure(EntityTypeBuilder<RawPayload> b)
    {
        b.ToTable("RawPayloads");
        b.HasIndex(p => p.ReceivedAtUtc).HasDatabaseName("IX_RawPayloads_ReceivedAt");
        b.HasIndex(p => new { p.VendorId, p.DeviceId }).HasDatabaseName("IX_RawPayloads_Vendor_Device");
        b.Property(p => p.Payload).HasColumnType("bytea");
    }
}
