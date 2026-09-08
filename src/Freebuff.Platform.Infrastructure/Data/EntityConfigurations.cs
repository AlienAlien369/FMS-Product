using Freebuff.Platform.Domain.Entities;
using ModuleConfigEntity = Freebuff.Platform.Domain.Entities.ModuleConfiguration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Freebuff.Platform.Infrastructure.Data;

public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> b)
    {
        b.HasIndex(c => c.Slug).IsUnique().HasFilter("\"Slug\" IS NOT NULL");
        b.Property(c => c.Name).HasMaxLength(200).IsRequired();
        b.HasQueryFilter(c => !c.IsDeleted);
    }
}

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.HasIndex(u => new { u.CompanyId, u.NormalizedEmail }).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.Property(u => u.Email).HasMaxLength(256).IsRequired();
        b.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        b.Property(u => u.LastName).HasMaxLength(100).IsRequired();
        b.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
        b.HasOne(u => u.Company).WithMany(c => c.Users).HasForeignKey(u => u.CompanyId);
        b.HasQueryFilter(u => !u.IsDeleted);
    }
}

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.HasIndex(r => new { r.CompanyId, r.Name }).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.Property(r => r.Name).HasMaxLength(100).IsRequired();
        b.HasOne(r => r.Company).WithMany(c => c.Roles).HasForeignKey(r => r.CompanyId);
        b.HasQueryFilter(r => !r.IsDeleted);
    }
}

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.HasIndex(p => p.Code).IsUnique();
        b.Property(p => p.Code).HasMaxLength(200).IsRequired();
        b.Property(p => p.Module).HasMaxLength(100).IsRequired();
        b.HasQueryFilter(p => !p.IsDeleted);
    }
}

public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.HasIndex(ur => new { ur.UserId, ur.RoleId }).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.HasOne(ur => ur.User).WithMany(u => u.UserRoles).HasForeignKey(ur => ur.UserId);
        b.HasOne(ur => ur.Role).WithMany(r => r.UserRoles).HasForeignKey(ur => ur.RoleId);
        b.HasQueryFilter(ur => !ur.IsDeleted);
    }
}

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.HasIndex(rp => new { rp.RoleId, rp.PermissionId }).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.HasOne(rp => rp.Role).WithMany(r => r.RolePermissions).HasForeignKey(rp => rp.RoleId);
        b.HasOne(rp => rp.Permission).WithMany(p => p.RolePermissions).HasForeignKey(rp => rp.PermissionId);
        b.HasQueryFilter(rp => !rp.IsDeleted);
    }
}

public class ModuleEntityTypeConfiguration : IEntityTypeConfiguration<Module>
{
    public void Configure(EntityTypeBuilder<Module> b)
    {
        b.HasIndex(m => m.Code).IsUnique();
        b.Property(m => m.Code).HasMaxLength(100).IsRequired();
        b.Property(m => m.Name).HasMaxLength(200).IsRequired();
        b.HasQueryFilter(m => !m.IsDeleted);
    }
}

public class PageConfiguration : IEntityTypeConfiguration<Page>
{
    public void Configure(EntityTypeBuilder<Page> b)
    {
        // Permission codes derive from the page key ("vehicle.view"), and
        // Permission.Code is globally unique, so the key must be globally unique.
        b.HasIndex(p => p.Key).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.Property(p => p.Key).HasMaxLength(100).IsRequired();
        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
        b.HasOne(p => p.Module).WithMany(m => m.Pages).HasForeignKey(p => p.ModuleId);
        b.HasQueryFilter(p => !p.IsDeleted);
    }
}

public class FeatureConfiguration : IEntityTypeConfiguration<Feature>
{
    public void Configure(EntityTypeBuilder<Feature> b)
    {
        b.HasIndex(f => new { f.ModuleId, f.Code }).IsUnique();
        b.Property(f => f.Code).HasMaxLength(100).IsRequired();
        b.Property(f => f.Name).HasMaxLength(200).IsRequired();
        b.HasOne(f => f.Module).WithMany(m => m.Features).HasForeignKey(f => f.ModuleId);
        b.HasQueryFilter(f => !f.IsDeleted);
    }
}

public class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> b)
    {
        b.HasIndex(p => p.Name).IsUnique();
        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
        b.Property(p => p.Price).HasPrecision(18, 2);
        b.HasQueryFilter(p => !p.IsDeleted);
    }
}

public class PackageFeatureConfiguration : IEntityTypeConfiguration<PackageFeature>
{
    public void Configure(EntityTypeBuilder<PackageFeature> b)
    {
        b.HasIndex(pf => new { pf.PackageId, pf.FeatureId }).IsUnique();
        b.HasOne(pf => pf.Package).WithMany(p => p.PackageFeatures).HasForeignKey(pf => pf.PackageId);
        b.HasOne(pf => pf.Feature).WithMany(f => f.PackageFeatures).HasForeignKey(pf => pf.FeatureId);
        b.HasQueryFilter(pf => !pf.IsDeleted);
    }
}

public class PackageModuleConfiguration : IEntityTypeConfiguration<PackageModule>
{
    public void Configure(EntityTypeBuilder<PackageModule> b)
    {
        b.HasIndex(pm => new { pm.PackageId, pm.ModuleId }).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.HasOne(pm => pm.Package).WithMany(p => p.PackageModules).HasForeignKey(pm => pm.PackageId);
        b.HasOne(pm => pm.Module).WithMany().HasForeignKey(pm => pm.ModuleId);
        b.HasQueryFilter(pm => !pm.IsDeleted);
    }
}

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> b)
    {
        b.Property(s => s.CurrentPrice).HasPrecision(18, 2);
        b.Property(s => s.DiscountPercentage).HasPrecision(5, 2);
        b.Property(s => s.TaxPercentage).HasPrecision(5, 2);
        b.HasOne(s => s.Company).WithMany().HasForeignKey(s => s.CompanyId);
        b.HasOne(s => s.Package).WithMany(p => p.Subscriptions).HasForeignKey(s => s.PackageId);
        b.HasQueryFilter(s => !s.IsDeleted);
    }
}

public class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> b)
    {
        b.HasIndex(v => new { v.CompanyId, v.RegistrationNumber }).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.Property(v => v.RegistrationNumber).HasMaxLength(50).IsRequired();
        b.HasOne(v => v.Company).WithMany(c => c.Vehicles).HasForeignKey(v => v.CompanyId);
        b.HasOne(v => v.Client).WithMany(c => c.Vehicles).HasForeignKey(v => v.ClientId);
        b.HasOne(v => v.Driver).WithMany(d => d.AssignedVehicles).HasForeignKey(v => v.DriverId);
        b.HasQueryFilter(v => !v.IsDeleted);
    }
}

public class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> b)
    {
        b.HasIndex(d => new { d.CompanyId, d.EmployeeId }).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.Property(d => d.EmployeeId).HasMaxLength(50).IsRequired();
        b.Property(d => d.FirstName).HasMaxLength(100).IsRequired();
        b.Property(d => d.LastName).HasMaxLength(100).IsRequired();
        b.HasOne(d => d.Company).WithMany(c => c.Drivers).HasForeignKey(d => d.CompanyId);
        b.HasQueryFilter(d => !d.IsDeleted);
    }
}

public class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> b)
    {
        b.Property(c => c.Name).HasMaxLength(200).IsRequired();
        b.HasOne(c => c.Company).WithMany(co => co.Clients).HasForeignKey(c => c.CompanyId);
        b.HasQueryFilter(c => !c.IsDeleted);
    }
}

public class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> b)
    {
        b.Property(t => t.Name).HasMaxLength(200).IsRequired();
        b.Property(t => t.StartLocation).HasMaxLength(500).IsRequired();
        b.HasOne(t => t.Company).WithMany(c => c.Trips).HasForeignKey(t => t.CompanyId);
        b.HasOne(t => t.Vehicle).WithMany(v => v.Trips).HasForeignKey(t => t.VehicleId);
        b.HasOne(t => t.Driver).WithMany(d => d.Trips).HasForeignKey(t => t.DriverId);
        b.HasOne(t => t.Client).WithMany(c => c.Trips).HasForeignKey(t => t.ClientId);
        b.HasOne(t => t.Route).WithMany(r => r.Trips).HasForeignKey(t => t.RouteId);
        b.HasMany(t => t.TripWaypoints).WithOne(w => w.Trip).HasForeignKey(w => w.TripId);
        b.HasMany(t => t.TripGeofences).WithOne(g => g.Trip).HasForeignKey(g => g.TripId);
        b.HasMany(t => t.StatusHistory).WithOne(h => h.Trip).HasForeignKey(h => h.TripId);
        b.HasQueryFilter(t => !t.IsDeleted);
    }
}

public class TripWaypointConfiguration : IEntityTypeConfiguration<TripWaypoint>
{
    public void Configure(EntityTypeBuilder<TripWaypoint> b)
    {
        b.Property(w => w.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(w => new { w.TripId, w.SequenceOrder }).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.HasQueryFilter(w => !w.IsDeleted);
    }
}

public class TripGeofenceConfiguration : IEntityTypeConfiguration<TripGeofence>
{
    public void Configure(EntityTypeBuilder<TripGeofence> b)
    {
        b.HasIndex(g => new { g.TripId, g.GeofenceId }).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.HasQueryFilter(g => !g.IsDeleted);
    }
}

public class TripStatusHistoryConfiguration : IEntityTypeConfiguration<TripStatusHistory>
{
    public void Configure(EntityTypeBuilder<TripStatusHistory> b)
    {
        b.HasIndex(h => h.TripId);
        // History is an audit log — never soft-deleted.
        b.HasQueryFilter(h => true);
    }
}

public class GeofenceConfiguration : IEntityTypeConfiguration<Geofence>
{
    public void Configure(EntityTypeBuilder<Geofence> b)
    {
        b.Property(g => g.Name).HasMaxLength(200).IsRequired();
        b.HasOne(g => g.Company).WithMany(c => c.Geofences).HasForeignKey(g => g.CompanyId);
        b.HasQueryFilter(g => !g.IsDeleted);
    }
}

public class ConfigurationConfiguration : IEntityTypeConfiguration<Configuration>
{
    public void Configure(EntityTypeBuilder<Configuration> b)
    {
        b.HasIndex(c => new { c.CompanyId, c.Key, c.Scope }).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.Property(c => c.Key).HasMaxLength(200).IsRequired();
        b.HasQueryFilter(c => !c.IsDeleted);
    }
}

public class ModuleEntityConfiguration : IEntityTypeConfiguration<ModuleConfigEntity>
{
    public void Configure(EntityTypeBuilder<ModuleConfigEntity> b)
    {
        b.HasKey(m => m.Id);
        b.HasOne(m => m.Company).WithMany().HasForeignKey(m => m.CompanyId);
        b.HasOne(m => m.Module).WithMany().HasForeignKey(m => m.ModuleId);
        b.HasQueryFilter(m => !m.IsDeleted);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId });
        b.HasIndex(a => a.CreatedAt);
        b.HasIndex(a => a.UserId);
        // Audit logs should never be soft-deleted
        b.HasQueryFilter(a => true);
    }
}

public class DriverScorePeriodConfiguration : IEntityTypeConfiguration<DriverScorePeriod>
{
    public void Configure(EntityTypeBuilder<DriverScorePeriod> b)
    {
        b.HasIndex(p => new { p.DriverId, p.Window, p.AnchorDate }).IsUnique();
        b.HasOne(p => p.Driver).WithMany().HasForeignKey(p => p.DriverId);
        b.HasOne(p => p.Company).WithMany().HasForeignKey(p => p.CompanyId);
    }
}

public class ScoreWeightConfigConfiguration : IEntityTypeConfiguration<ScoreWeightConfig>
{
    public void Configure(EntityTypeBuilder<ScoreWeightConfig> b)
    {
        // One override row per company; CompanyId = null is the single platform default.
        b.HasIndex(c => c.CompanyId).IsUnique();
        b.HasOne(c => c.Company).WithMany().HasForeignKey(c => c.CompanyId).IsRequired(false);
    }
}

public class FuelRecordConfiguration : IEntityTypeConfiguration<FuelRecord>
{
    public void Configure(EntityTypeBuilder<FuelRecord> b)
    {
        b.HasIndex(f => new { f.CompanyId, f.VehicleId, f.RecordDate });
        b.HasOne(f => f.Vehicle).WithMany(v => v.FuelRecords).HasForeignKey(f => f.VehicleId);
        b.HasQueryFilter(f => !f.IsDeleted);
    }
}

public class FuelConsumptionSnapshotConfiguration : IEntityTypeConfiguration<FuelConsumptionSnapshot>
{
    public void Configure(EntityTypeBuilder<FuelConsumptionSnapshot> b)
    {
        b.HasKey(s => s.Id);
        b.HasIndex(s => new { s.VehicleId, s.EventTimeUtc });
        b.HasIndex(s => new { s.TenantId, s.EventTimeUtc });
    }
}

public class MaintenanceScheduleConfiguration : IEntityTypeConfiguration<MaintenanceSchedule>
{
    public void Configure(EntityTypeBuilder<MaintenanceSchedule> b)
    {
        b.HasIndex(s => new { s.CompanyId, s.VehicleId });
        b.HasOne(s => s.Vehicle).WithMany().HasForeignKey(s => s.VehicleId);
        b.HasQueryFilter(s => !s.IsDeleted);
    }
}

public class MaintenanceRecordConfiguration : IEntityTypeConfiguration<MaintenanceRecord>
{
    public void Configure(EntityTypeBuilder<MaintenanceRecord> b)
    {
        b.HasIndex(r => new { r.CompanyId, r.VehicleId, r.CompletedDate });
        b.HasOne(r => r.Vehicle).WithMany(v => v.MaintenanceRecords).HasForeignKey(r => r.VehicleId);
        b.HasOne(r => r.MaintenanceSchedule).WithMany().HasForeignKey(r => r.MaintenanceScheduleId);
        b.HasQueryFilter(r => !r.IsDeleted);
    }
}

public class AlertTypeConfiguration : IEntityTypeConfiguration<AlertType>
{
    public void Configure(EntityTypeBuilder<AlertType> b)
    {
        b.HasIndex(a => a.Code).IsUnique();
        b.HasQueryFilter(a => !a.IsDeleted);
        b.Property(a => a.NonMutablePriority).HasDefaultValue(false);
    }
}

public class CompanyAlertSubscriptionConfiguration : IEntityTypeConfiguration<CompanyAlertSubscription>
{
    public void Configure(EntityTypeBuilder<CompanyAlertSubscription> b)
    {
        b.HasIndex(c => new { c.CompanyId, c.AlertTypeId }).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.HasOne(c => c.Company).WithMany().HasForeignKey(c => c.CompanyId);
        b.HasOne(c => c.AlertType).WithMany().HasForeignKey(c => c.AlertTypeId);
        b.HasQueryFilter(c => !c.IsDeleted);
    }
}

public class RoleAlertVisibilityConfiguration : IEntityTypeConfiguration<RoleAlertVisibility>
{
    public void Configure(EntityTypeBuilder<RoleAlertVisibility> b)
    {
        b.HasIndex(r => new { r.CompanyId, r.RoleId, r.AlertTypeId }).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.HasOne(r => r.Company).WithMany().HasForeignKey(r => r.CompanyId);
        b.HasOne(r => r.Role).WithMany().HasForeignKey(r => r.RoleId);
        b.HasOne(r => r.AlertType).WithMany().HasForeignKey(r => r.AlertTypeId);
        b.HasQueryFilter(r => !r.IsDeleted);
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt });
        b.HasOne(n => n.User).WithMany(u => u.Notifications).HasForeignKey(n => n.UserId);
        b.HasQueryFilter(n => !n.IsDeleted);
    }
}

public class NotificationEventTypeConfiguration : IEntityTypeConfiguration<NotificationEventType>
{
    public void Configure(EntityTypeBuilder<NotificationEventType> b)
    {
        b.HasIndex(e => e.Code).IsUnique();
        b.Property(e => e.Code).HasMaxLength(200).IsRequired();
        b.Property(e => e.Name).HasMaxLength(200).IsRequired();
        b.HasQueryFilter(e => !e.IsDeleted);
    }
}

public class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> b)
    {
        b.HasIndex(p => new { p.UserId, p.EventType }).IsUnique().HasFilter("\"IsDeleted\" = false");
        b.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId);
        b.HasQueryFilter(p => !p.IsDeleted);
    }
}

public class ScheduledReportConfiguration : IEntityTypeConfiguration<ScheduledReport>
{
    public void Configure(EntityTypeBuilder<ScheduledReport> b)
    {
        b.HasIndex(s => new { s.CompanyId, s.CreatedByUserId });
        // The runner ticks over NextRunAt — keep the hot scan narrow.
        b.HasIndex(s => new { s.NextRunAt, s.IsActive, s.IsPausedAfterError });
        b.HasOne(s => s.Company).WithMany().HasForeignKey(s => s.CompanyId);
        b.HasOne(s => s.CreatedByUser).WithMany().HasForeignKey(s => s.CreatedByUserId);
        b.HasQueryFilter(s => !s.IsDeleted);
    }
}
