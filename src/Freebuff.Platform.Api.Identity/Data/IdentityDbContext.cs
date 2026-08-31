using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Identity.Data;

/// <summary>
/// Identity Service DbContext - manages Users, Roles, Permissions, UserRoles, RolePermissions.
/// </summary>
public class IdentityDbContext : DbContext
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(b =>
        {
            b.HasIndex(u => new { u.CompanyId, u.NormalizedEmail }).IsUnique();
            b.Property(u => u.Email).HasMaxLength(256).IsRequired();
            b.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
            b.Property(u => u.LastName).HasMaxLength(100).IsRequired();
            b.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
            b.HasQueryFilter(u => !u.IsDeleted);
        });

        modelBuilder.Entity<Role>(b =>
        {
            b.HasIndex(r => new { r.CompanyId, r.Name }).IsUnique();
            b.Property(r => r.Name).HasMaxLength(100).IsRequired();
            b.HasQueryFilter(r => !r.IsDeleted);
        });

        modelBuilder.Entity<Permission>(b =>
        {
            b.HasIndex(p => p.Code).IsUnique();
            b.Property(p => p.Code).HasMaxLength(200).IsRequired();
            b.HasQueryFilter(p => !p.IsDeleted);
        });

        modelBuilder.Entity<UserRole>(b =>
        {
            b.HasIndex(ur => new { ur.UserId, ur.RoleId }).IsUnique();
            b.HasQueryFilter(ur => !ur.IsDeleted);
        });

        modelBuilder.Entity<RolePermission>(b =>
        {
            b.HasIndex(rp => new { rp.RoleId, rp.PermissionId }).IsUnique();
            b.HasQueryFilter(rp => !rp.IsDeleted);
        });

        modelBuilder.Entity<AuditLog>(b =>
        {
            b.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId });
            b.HasIndex(a => a.CreatedAt);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entry.Entity.IsDeleted = true;
                entry.Entity.DeletedAt = now;
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
