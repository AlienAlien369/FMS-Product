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
