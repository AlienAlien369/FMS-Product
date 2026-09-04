using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    private readonly ITenantContext? _tenantContext;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ITenantContext? tenantContext = null)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    // Core
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    // Platform
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<Feature> Features => Set<Feature>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<PackageModule> PackageModules => Set<PackageModule>();
    public DbSet<PackageFeature> PackageFeatures => Set<PackageFeature>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<ModuleConfiguration> ModuleConfigurations => Set<ModuleConfiguration>();
    public DbSet<Configuration> Configurations => Set<Configuration>();

    // Fleet
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<Geofence> Geofences => Set<Geofence>();
    public DbSet<VehicleGeofence> VehicleGeofences => Set<VehicleGeofence>();
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<RouteVehicle> RouteVehicles => Set<RouteVehicle>();

    // Monitoring
    public DbSet<AlertConfiguration> AlertConfigurations => Set<AlertConfiguration>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<FuelRecord> FuelRecords => Set<FuelRecord>();
    public DbSet<MaintenanceRecord> MaintenanceRecords => Set<MaintenanceRecord>();
    public DbSet<Document> Documents => Set<Document>();

    // Localization
    public DbSet<Language> Languages => Set<Language>();
    public DbSet<Currency> Currencies => Set<Currency>();

    // Device abstraction layer
    public DbSet<DeviceVendor> DeviceVendors => Set<DeviceVendor>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceSim> DeviceSims => Set<DeviceSim>();
    public DbSet<VehicleDevice> VehicleDevices => Set<VehicleDevice>();
    public DbSet<TelemetryEvent> TelemetryEvents => Set<TelemetryEvent>();
    public DbSet<TelemetryState> TelemetryStates => Set<TelemetryState>();
    public DbSet<RawPayload> RawPayloads => Set<RawPayload>();

    // Audit
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyMultiTenancy();
        ApplyAuditInfo();
        ApplySoftDeleteFilter();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyMultiTenancy()
    {
        var tenantId = _tenantContext?.TenantId;
        var now = DateTime.UtcNow;
        var userId = _tenantContext?.UserId;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == null && tenantId != null)
                entry.Entity.TenantId = tenantId;
        }

        // Also for AuditLog which doesn't extend BaseEntity
        foreach (var entry in ChangeTracker.Entries<AuditLog>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.TenantId == null && tenantId != null)
                    entry.Entity.TenantId = tenantId;
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = userId;
            }
        }
    }

    private void ApplyAuditInfo()
    {
        var now = DateTime.UtcNow;
        var userId = _tenantContext?.UserId;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = userId;
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = userId;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = userId;
            }
        }
    }

    private void ApplySoftDeleteFilter()
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entry.Entity.IsDeleted = true;
                entry.Entity.DeletedAt = DateTime.UtcNow;
                entry.Entity.DeletedBy = _tenantContext?.UserId;
            }
        }
    }
}
