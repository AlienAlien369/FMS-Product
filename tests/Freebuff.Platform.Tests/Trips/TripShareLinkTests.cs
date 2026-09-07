using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Freebuff.Platform.Tests.Trips;

/// <summary>
/// Customer Tracking Link share-token primitive: unguessable (256-bit URL-safe)
/// tokens, optional expiry (default = trip completion + N days), revocation that
/// takes effect on the next request, and possession-only validation — the first
/// access primitive that is NOT a user/role/company member.
/// </summary>
public class TripShareLinkTests
{
    private static ApplicationDbContext NewDb(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<(ApplicationDbContext Db, Guid TripId)> SeedCompletedTripAsync(string name)
    {
        var db = NewDb(name);
        var company = Guid.NewGuid();
        var trip = new Trip
        {
            Id = Guid.NewGuid(), Name = "Shipment", Status = TripStatus.Completed,
            CompanyId = company, TenantId = company,
            VehicleId = Guid.NewGuid(), DriverId = Guid.NewGuid(),
            StartLocation = "Depot", StartLatitude = 23.0, StartLongitude = 72.5,
            ActualStartTime = DateTime.UtcNow.AddHours(-4),
            ActualEndTime = DateTime.UtcNow.AddHours(-1),
        };
        trip.TripWaypoints.Add(new TripWaypoint
        {
            Id = Guid.NewGuid(), TripId = trip.Id, SequenceOrder = 1,
            Name = "Customer", Latitude = 23.05, Longitude = 72.62, WaypointType = TripWaypointType.Delivery,
        });
        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        return (db, trip.Id);
    }

    // ── Token generation: unguessable, URL-safe, unique ───────────────────

    [Fact]
    public async Task Generate_CreatesUrlSafeUnguessableToken()
    {
        var (db, tripId) = await SeedCompletedTripAsync("link_token_" + Guid.NewGuid());
        var svc = new TripShareLinkService(db);

        var link = await svc.GenerateAsync(tripId, "admin-1");

        Assert.NotNull(link.Token);
        Assert.Matches("^[A-Za-z0-9_-]{40,}$", link.Token); // URL-safe base64, no +/=
        Assert.DoesNotContain('+', link.Token);
        Assert.DoesNotContain('/', link.Token);
        Assert.DoesNotContain('=', link.Token);
        Assert.Equal("admin-1", link.CreatedByUserId); // dedicated column — BaseEntity.CreatedBy is audit-owned
        Assert.False(link.IsRevoked);
    }

    [Fact]
    public async Task Generate_TokensAreUniquePerLink()
    {
        var (db, tripId) = await SeedCompletedTripAsync("link_unique_" + Guid.NewGuid());
        var svc = new TripShareLinkService(db);

        var a = await svc.GenerateAsync(tripId, "admin-1");
        var b = await svc.GenerateAsync(tripId, "admin-1");

        Assert.NotEqual(a.Token, b.Token);
        Assert.Equal(2, await db.TripShareLinks.CountAsync());
    }

    [Fact]
    public async Task Generate_DefaultExpiry_IsTripCompletionPlusLifetime()
    {
        var (db, tripId) = await SeedCompletedTripAsync("link_exp_" + Guid.NewGuid());
        var svc = new TripShareLinkService(db);
        var completedAt = DateTime.UtcNow.AddHours(-1);

        var link = await svc.GenerateAsync(tripId, "admin-1");

        Assert.NotNull(link.ExpiresAt);
        var expected = completedAt.AddDays(TripShareLinkService.DefaultLinkLifetimeDays);
        Assert.True(link.ExpiresAt.Value >= expected.AddMinutes(-2) && link.ExpiresAt.Value <= expected.AddMinutes(2),
            $"expiry {link.ExpiresAt} should be completion + {TripShareLinkService.DefaultLinkLifetimeDays} days");
    }

    [Fact]
    public async Task Generate_ExplicitExpiryDays_OverridesDefault()
    {
        var (db, tripId) = await SeedCompletedTripAsync("link_exp2_" + Guid.NewGuid());
        var svc = new TripShareLinkService(db);

        var link = await svc.GenerateAsync(tripId, "admin-1", expiresInDays: 7);

        Assert.NotNull(link.ExpiresAt);
        Assert.True(link.ExpiresAt.Value >= DateTime.UtcNow.AddDays(6.99) && link.ExpiresAt.Value <= DateTime.UtcNow.AddDays(7.01));
    }

    // ── Possession-only validation ─────────────────────────────────────────

    [Fact]
    public async Task Resolve_ValidToken_ReturnsLinkAndTrip()
    {
        var (db, tripId) = await SeedCompletedTripAsync("link_ok_" + Guid.NewGuid());
        var svc = new TripShareLinkService(db);
        var link = await svc.GenerateAsync(tripId, "admin-1");

        var resolved = await svc.ResolveValidAsync(link.Token);

        Assert.NotNull(resolved);
        Assert.Equal(tripId, resolved.Value.Trip.Id);
        Assert.False(resolved.Value.Link.IsRevoked);
    }

    [Fact]
    public async Task Resolve_UnknownOrMalformedToken_ReturnsNull()
    {
        var (db, tripId) = await SeedCompletedTripAsync("link_unknown_" + Guid.NewGuid());
        var svc = new TripShareLinkService(db);

        Assert.Null(await svc.ResolveValidAsync("definitely-not-a-real-token"));
        Assert.Null(await svc.ResolveValidAsync(""));
        Assert.Null(await svc.ResolveValidAsync(null!));
    }

    [Fact]
    public async Task Resolve_RevokedLink_ReturnsNull_Immediately()
    {
        var (db, tripId) = await SeedCompletedTripAsync("link_rev_" + Guid.NewGuid());
        var svc = new TripShareLinkService(db);
        var link = await svc.GenerateAsync(tripId, "admin-1");

        var revoked = await svc.RevokeAsync(tripId, link.Id, "admin-1");
        Assert.True(revoked);

        // Revocation is a DB-backed check on every request — no caching window.
        Assert.Null(await svc.ResolveValidAsync(link.Token));
    }

    [Fact]
    public async Task Resolve_ExpiredLink_ReturnsNull()
    {
        var (db, tripId) = await SeedCompletedTripAsync("link_dead_" + Guid.NewGuid());
        var svc = new TripShareLinkService(db);
        var link = await svc.GenerateAsync(tripId, "admin-1");
        link.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        Assert.Null(await svc.ResolveValidAsync(link.Token));
    }

    [Fact]
    public async Task Resolve_LinkForDeletedTrip_ReturnsNull()
    {
        var (db, tripId) = await SeedCompletedTripAsync("link_deltrip_" + Guid.NewGuid());
        var svc = new TripShareLinkService(db);
        var link = await svc.GenerateAsync(tripId, "admin-1");
        var trip = await db.Trips.FirstAsync(t => t.Id == tripId);
        trip.IsDeleted = true;
        await db.SaveChangesAsync();

        Assert.Null(await svc.ResolveValidAsync(link.Token));
    }

    // ── Audit list ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ListForTrip_ReturnsOnlyActiveLinks()
    {
        var (db, tripId) = await SeedCompletedTripAsync("link_list_" + Guid.NewGuid());
        var svc = new TripShareLinkService(db);
        var active = await svc.GenerateAsync(tripId, "admin-1");
        var revoked = await svc.GenerateAsync(tripId, "admin-2");
        await svc.RevokeAsync(tripId, revoked.Id, "admin-2");

        var links = await svc.ListForTripAsync(tripId);

        var token = Assert.Single(links);
        Assert.Equal(active.Token, token.Token);
    }

    [Fact]
    public async Task Revoke_UnknownLink_ReturnsFalse()
    {
        var (db, tripId) = await SeedCompletedTripAsync("link_norev_" + Guid.NewGuid());
        var svc = new TripShareLinkService(db);

        Assert.False(await svc.RevokeAsync(tripId, Guid.NewGuid(), "admin-1"));
    }
}