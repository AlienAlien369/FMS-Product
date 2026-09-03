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
        b.HasQueryFilter(t => !t.IsDeleted);
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
