using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Fleet.Data;

/// <summary>
/// Fleet Service DbContext - manages Vehicles, Drivers, Trips, Geofences, Clients.
/// Company navigation properties are ignored since Company lives in Tenant DB.
/// </summary>
public class FleetDbContext : DbContext
{
    public FleetDbContext(DbContextOptions<FleetDbContext> options) : base(options) { }

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<Geofence> Geofences => Set<Geofence>();
    public DbSet<VehicleGeofence> VehicleGeofences => Set<VehicleGeofence>();

    // Device abstraction layer (mirrors ApplicationDbContext — see
    // Infrastructure/Data/DeviceConfigurations.cs for the canonical shapes).
    public DbSet<DeviceVendor> DeviceVendors => Set<DeviceVendor>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceSim> DeviceSims => Set<DeviceSim>();
    public DbSet<VehicleDevice> VehicleDevices => Set<VehicleDevice>();
    public DbSet<TelemetryEvent> TelemetryEvents => Set<TelemetryEvent>();
    public DbSet<TelemetryState> TelemetryStates => Set<TelemetryState>();
    public DbSet<RawPayload> RawPayloads => Set<RawPayload>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Ignore navigation properties to entities in other service databases
        modelBuilder.Entity<Vehicle>().Ignore(v => v.Company);
        modelBuilder.Entity<Vehicle>().Ignore(v => v.Client);
        modelBuilder.Entity<Vehicle>().Ignore(v => v.FuelRecords);
        modelBuilder.Entity<Vehicle>().Ignore(v => v.MaintenanceRecords);
        modelBuilder.Entity<Driver>().Ignore(d => d.Company);
        modelBuilder.Entity<Client>().Ignore(c => c.Company);
        modelBuilder.Entity<Client>().Ignore(c => c.Vehicles);
        modelBuilder.Entity<Client>().Ignore(c => c.Trips);
        modelBuilder.Entity<Trip>().Ignore(t => t.Company);
        modelBuilder.Entity<Trip>().Ignore(t => t.Client);
        modelBuilder.Entity<Geofence>().Ignore(g => g.Company);

        modelBuilder.Entity<Vehicle>(b =>
        {
            b.HasIndex(v => new { v.CompanyId, v.RegistrationNumber }).IsUnique();
            b.Property(v => v.RegistrationNumber).HasMaxLength(50).IsRequired();
            b.HasQueryFilter(v => !v.IsDeleted);
        });

        modelBuilder.Entity<Driver>(b =>
        {
            b.HasIndex(d => new { d.CompanyId, d.EmployeeId }).IsUnique();
            b.Property(d => d.EmployeeId).HasMaxLength(50).IsRequired();
            b.Property(d => d.FirstName).HasMaxLength(100).IsRequired();
            b.Property(d => d.LastName).HasMaxLength(100).IsRequired();
            b.HasQueryFilter(d => !d.IsDeleted);
        });

        modelBuilder.Entity<Client>(b =>
        {
            b.Property(c => c.Name).HasMaxLength(200).IsRequired();
            b.HasQueryFilter(c => !c.IsDeleted);
        });

        modelBuilder.Entity<Trip>(b =>
        {
            b.Property(t => t.Name).HasMaxLength(200).IsRequired();
            b.Property(t => t.StartLocation).HasMaxLength(500).IsRequired();
            b.HasQueryFilter(t => !t.IsDeleted);
        });

        modelBuilder.Entity<Geofence>(b =>
        {
            b.Property(g => g.Name).HasMaxLength(200).IsRequired();
            b.HasQueryFilter(g => !g.IsDeleted);
        });

        modelBuilder.Entity<VehicleGeofence>(b =>
        {
            b.HasQueryFilter(vg => !vg.IsDeleted);
        });

        // ── Device Abstraction Layer (shapes must match DeviceConfigurations.cs) ──
        modelBuilder.Entity<DeviceVendor>(b =>
        {
            b.ToTable("DeviceVendors");
            b.HasIndex(v => v.Code).IsUnique().HasDatabaseName("IX_DeviceVendors_Code").HasFilter("\"IsDeleted\" = false");
            b.Property(v => v.Code).HasMaxLength(50).IsRequired();
            b.Property(v => v.Name).HasMaxLength(200).IsRequired();
            b.HasQueryFilter(v => !v.IsDeleted);
        });

        modelBuilder.Entity<Device>(b =>
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
            b.HasQueryFilter(d => !d.IsDeleted);
        });

        modelBuilder.Entity<DeviceSim>(b =>
        {
            b.ToTable("DeviceSims");
            b.HasIndex(s => s.DeviceId).HasDatabaseName("IX_DeviceSims_DeviceId");
            b.HasIndex(s => s.DeviceId).IsUnique().HasDatabaseName("UX_DeviceSims_ActivePrimary").HasFilter("\"IsPrimary\" = true AND \"IsDeleted\" = false");
            b.Property(s => s.Status).HasConversion<int>();
            b.HasQueryFilter(s => !s.IsDeleted);
        });

        modelBuilder.Entity<VehicleDevice>(b =>
        {
            b.ToTable("VehicleDevices");
            b.HasIndex(vd => vd.VehicleId).HasDatabaseName("IX_VehicleDevices_VehicleId");
            b.HasIndex(vd => vd.DeviceId).HasDatabaseName("IX_VehicleDevices_DeviceId");
            b.HasIndex(vd => new { vd.VehicleId, vd.Role })
                .IsUnique()
                .HasDatabaseName("UX_VehicleDevices_Vehicle_Role_Active")
                .HasFilter("\"AssignedTo\" IS NULL AND \"IsDeleted\" = false");
            b.Property(vd => vd.Role).HasConversion<int>();
            b.HasQueryFilter(vd => !vd.IsDeleted);
        });

        modelBuilder.Entity<TelemetryEvent>(b =>
        {
            b.ToTable("TelemetryEvents");
            b.HasIndex(e => e.DeviceId).HasDatabaseName("IX_TelemetryEvents_DeviceId");
            b.HasIndex(e => new { e.VehicleId, e.EventTimeUtc }).HasDatabaseName("IX_TelemetryEvents_Vehicle_Time");
        });

        modelBuilder.Entity<TelemetryState>(b =>
        {
            b.ToTable("TelemetryStates");
            b.HasIndex(s => s.VehicleId).IsUnique().HasDatabaseName("UX_TelemetryStates_VehicleId");
            b.HasIndex(s => s.DeviceId).HasDatabaseName("IX_TelemetryStates_DeviceId");
        });

        modelBuilder.Entity<RawPayload>(b =>
        {
            b.ToTable("RawPayloads");
            b.HasIndex(p => p.ReceivedAtUtc).HasDatabaseName("IX_RawPayloads_ReceivedAt");
            b.HasIndex(p => new { p.VendorId, p.DeviceId }).HasDatabaseName("IX_RawPayloads_Vendor_Device");
            b.Property(p => p.Payload).HasColumnType("bytea");
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added) { entry.Entity.CreatedAt = now; entry.Entity.UpdatedAt = now; }
            else if (entry.State == EntityState.Modified) { entry.Entity.UpdatedAt = now; }
            else if (entry.State == EntityState.Deleted) { entry.State = EntityState.Modified; entry.Entity.IsDeleted = true; entry.Entity.DeletedAt = now; }
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
