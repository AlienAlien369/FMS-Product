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
        b.HasMany(e => e.TyrePressureReadings).WithOne(r => r.TelemetryEvent)
            .HasForeignKey(r => r.TelemetryEventId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class TyrePressureReadingConfiguration : IEntityTypeConfiguration<TyrePressureReading>
{
    public void Configure(EntityTypeBuilder<TyrePressureReading> b)
    {
        b.ToTable("TyrePressureReadings");
        b.Property(r => r.Position).HasConversion<int>();
        b.HasIndex(r => new { r.VehicleId, r.EventTimeUtc }).HasDatabaseName("IX_TyrePressureReadings_Vehicle_Time");
        b.HasOne(r => r.TelemetryEvent).WithMany(e => e.TyrePressureReadings)
            .HasForeignKey(r => r.TelemetryEventId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class TelemetryRollupHourlyConfiguration : IEntityTypeConfiguration<TelemetryRollupHourly>
{
    public void Configure(EntityTypeBuilder<TelemetryRollupHourly> b)
    {
        b.ToTable("TelemetryRollupsHourly");
        b.Property(r => r.TyrePosition).HasConversion<int?>();
        b.HasIndex(r => new { r.VehicleId, r.SensorType, r.TyrePosition, r.HourBucketUtc })
            .IsUnique()
            .HasDatabaseName("UX_TelemetryRollups_Vehicle_Sensor_Hour");
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

public class DriverBehaviorEventConfiguration : IEntityTypeConfiguration<DriverBehaviorEvent>
{
    public void Configure(EntityTypeBuilder<DriverBehaviorEvent> b)
    {
        b.ToTable("DriverBehaviorEvents");
        b.HasIndex(e => new { e.DriverId, e.EventTimeUtc }).HasDatabaseName("IX_DriverBehaviorEvents_Driver_Time");
        b.HasIndex(e => new { e.VehicleId, e.EventTimeUtc }).HasDatabaseName("IX_DriverBehaviorEvents_Vehicle_Time");
        b.Property(e => e.EventType).HasConversion<int>();
        b.HasIndex(e => e.DeviceId).HasDatabaseName("IX_DriverBehaviorEvents_DeviceId");
        b.HasOne(e => e.Vehicle).WithMany().HasForeignKey(e => e.VehicleId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(e => e.Driver).WithMany().HasForeignKey(e => e.DriverId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class ProofOfDeliveryConfiguration : IEntityTypeConfiguration<ProofOfDelivery>
{
    public void Configure(EntityTypeBuilder<ProofOfDelivery> b)
    {
        b.ToTable("ProofOfDeliveries");
        b.HasIndex(p => p.TripId).HasDatabaseName("IX_ProofOfDeliveries_TripId");
        b.HasIndex(p => p.WaypointId).HasDatabaseName("IX_ProofOfDeliveries_WaypointId");
        b.HasIndex(p => new { p.WaypointId, p.Type })
            .IsUnique()
            .HasDatabaseName("UX_ProofOfDeliveries_Waypoint_Type_Active")
            .HasFilter("\"IsDeleted\" = false");
        b.Property(p => p.Type).HasConversion<int>();
        b.Property(p => p.OtpFailedAttempts).HasDefaultValue(0);
        b.HasOne(p => p.Trip).WithMany().HasForeignKey(p => p.TripId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(p => p.Waypoint).WithMany(w => w.ProofOfDeliveries).HasForeignKey(p => p.WaypointId).OnDelete(DeleteBehavior.Cascade);
        b.HasQueryFilter(p => !p.IsDeleted);
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
