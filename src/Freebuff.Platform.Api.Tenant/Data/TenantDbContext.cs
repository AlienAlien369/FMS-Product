using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Tenant.Data;

/// <summary>
/// Tenant Service DbContext - manages Companies, Packages, Subscriptions, Modules, Features, Configurations.
/// </summary>
public class TenantDbContext : DbContext
{
    public TenantDbContext(DbContextOptions<TenantDbContext> options) : base(options) { }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<PackageFeature> PackageFeatures => Set<PackageFeature>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<Feature> Features => Set<Feature>();
    public DbSet<ModuleConfiguration> ModuleConfigurations => Set<ModuleConfiguration>();
    public DbSet<Configuration> Configurations => Set<Configuration>();
    public DbSet<Language> Languages => Set<Language>();
    public DbSet<Currency> Currencies => Set<Currency>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Company>(b =>
        {
            b.HasIndex(c => c.Slug).IsUnique().HasFilter("\"Slug\" IS NOT NULL");
            b.Property(c => c.Name).HasMaxLength(200).IsRequired();
            b.HasQueryFilter(c => !c.IsDeleted);
        });

        modelBuilder.Entity<Package>(b =>
        {
            b.HasIndex(p => p.Name).IsUnique();
            b.Property(p => p.Name).HasMaxLength(200).IsRequired();
            b.Property(p => p.Price).HasPrecision(18, 2);
            b.HasQueryFilter(p => !p.IsDeleted);
        });

        modelBuilder.Entity<Subscription>(b =>
        {
            b.Property(s => s.CurrentPrice).HasPrecision(18, 2);
            b.HasQueryFilter(s => !s.IsDeleted);
        });

        modelBuilder.Entity<Module>(b =>
        {
            b.HasIndex(m => m.Code).IsUnique();
            b.Property(m => m.Code).HasMaxLength(100).IsRequired();
            b.HasQueryFilter(m => !m.IsDeleted);
        });

        modelBuilder.Entity<Feature>(b =>
        {
            b.HasIndex(f => new { f.ModuleId, f.Code }).IsUnique();
            b.HasQueryFilter(f => !f.IsDeleted);
        });

        modelBuilder.Entity<Language>(b =>
        {
            b.HasIndex(l => l.Code).IsUnique();
            b.Property(l => l.Code).HasMaxLength(10).IsRequired();
        });

        modelBuilder.Entity<Currency>(b =>
        {
            b.HasIndex(c => c.Code).IsUnique();
            b.Property(c => c.Code).HasMaxLength(10).IsRequired();
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
