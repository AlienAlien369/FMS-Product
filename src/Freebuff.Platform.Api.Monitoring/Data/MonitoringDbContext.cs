using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Monitoring.Data;

/// <summary>
/// Monitoring Service DbContext - manages Alerts, Notifications, Fuel, Maintenance, Documents.
/// Navigation properties to Company, Vehicle, Driver are ignored (they live in other services).
/// </summary>
public class MonitoringDbContext : DbContext
{
    public MonitoringDbContext(DbContextOptions<MonitoringDbContext> options) : base(options) { }

    public DbSet<AlertConfiguration> AlertConfigurations => Set<AlertConfiguration>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<FuelRecord> FuelRecords => Set<FuelRecord>();
    public DbSet<MaintenanceRecord> MaintenanceRecords => Set<MaintenanceRecord>();
    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Ignore navigation properties to entities in other service databases
        modelBuilder.Entity<Alert>().Ignore(a => a.Company);
        modelBuilder.Entity<Alert>().Ignore(a => a.Vehicle);
        modelBuilder.Entity<Alert>().Ignore(a => a.Driver);
        modelBuilder.Entity<Alert>().Ignore(a => a.AlertConfiguration);
        modelBuilder.Entity<AlertConfiguration>().Ignore(ac => ac.Company);
        modelBuilder.Entity<FuelRecord>().Ignore(f => f.Vehicle);
        modelBuilder.Entity<MaintenanceRecord>().Ignore(m => m.Vehicle);
        modelBuilder.Entity<Notification>().Ignore(n => n.User);

        modelBuilder.Entity<Alert>(b =>
        {
            b.Property(a => a.Title).HasMaxLength(200).IsRequired();
            b.HasQueryFilter(a => !a.IsDeleted);
        });

        modelBuilder.Entity<AlertConfiguration>(b =>
        {
            b.Property(ac => ac.Name).HasMaxLength(200).IsRequired();
            b.HasQueryFilter(ac => !ac.IsDeleted);
        });

        modelBuilder.Entity<Notification>(b =>
        {
            b.Property(n => n.Title).HasMaxLength(200).IsRequired();
            b.HasQueryFilter(n => !n.IsDeleted);
        });

        modelBuilder.Entity<FuelRecord>(b =>
        {
            b.Property(f => f.Quantity).HasPrecision(18, 2);
            b.HasQueryFilter(f => !f.IsDeleted);
        });

        modelBuilder.Entity<MaintenanceRecord>(b =>
        {
            b.Property(m => m.Title).HasMaxLength(200).IsRequired();
            b.HasQueryFilter(m => !m.IsDeleted);
        });

        modelBuilder.Entity<Document>(b =>
        {
            b.Property(d => d.FileName).HasMaxLength(500).IsRequired();
            b.HasQueryFilter(d => !d.IsDeleted);
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
