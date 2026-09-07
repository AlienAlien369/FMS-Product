using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Freebuff.Platform.Tests.Trips;

/// <summary>
/// Proof-of-delivery capture: signature / photo / OTP, hashed OTP storage,
/// geolocation mismatch flagging (data-quality signal, never a hard block),
/// and the delivery-waypoint arrival gate (company default + per-trip override).
/// </summary>
public class ProofOfDeliveryTests
{
    private static ApplicationDbContext NewDb(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<(ApplicationDbContext Db, Guid TripId, Guid WaypointId, Guid CompanyId)> SeedTripWithDeliveryWaypointAsync(string name, double wpLat = 23.0, double wpLng = 72.0, TripWaypointType type = TripWaypointType.Delivery)
    {
        var db = NewDb(name);
        var company = Guid.NewGuid();
        var trip = new Trip
        {
            Id = Guid.NewGuid(), Name = "Delivery run", Status = TripStatus.InProgress,
            CompanyId = company, TenantId = company,
            VehicleId = Guid.NewGuid(), DriverId = Guid.NewGuid(),
            StartLocation = "Depot", StartLatitude = 23.0, StartLongitude = 72.5,
            EndLocation = "Customer", EndLatitude = wpLat, EndLongitude = wpLng,
        };
        var waypoint = new TripWaypoint
        {
            Id = Guid.NewGuid(), TripId = trip.Id, SequenceOrder = 1,
            Name = "Customer site", Latitude = wpLat, Longitude = wpLng,
            WaypointType = type, CustomerPhone = "+919999999999", CustomerEmail = "cust@example.com",
        };
        trip.TripWaypoints.Add(waypoint);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        return (db, trip.Id, waypoint.Id, company);
    }

    private static async Task SetCompanyRequirePodAsync(ApplicationDbContext db, Guid company, bool value)
    {
        db.Configurations.Add(new Configuration
        {
            Id = Guid.NewGuid(), Key = "fleet.require_pod_for_delivery",
            Value = value ? "true" : "false",
            Scope = ConfigurationScope.Company, ScopeEntityId = company, CompanyId = company,
            Module = "fleet",
        });
        await db.SaveChangesAsync();
    }

    // ── Capture types ──────────────────────────────────────────────────────

    [Fact]
    public async Task CaptureSignature_StoresSvg_AndCountsAsEvidence()
    {
        var (db, tripId, wpId, _) = await SeedTripWithDeliveryWaypointAsync("pod_sig_" + Guid.NewGuid());
        var svc = new ProofOfDeliveryService(db);

        var record = await svc.CaptureSignatureAsync(tripId, wpId, "driver-1", "<svg>…</svg>", 23.001, 72.001, "signed at door");

        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.Equal(ProofOfDeliveryType.Signature, record.Type);
        Assert.Equal("<svg>…</svg>", record.SignatureSvg);
        Assert.Equal("driver-1", record.CapturedBy);
        Assert.Equal("signed at door", record.Notes);
        Assert.True(await svc.HasVerifiedEvidenceAsync(tripId, wpId));
    }

    [Fact]
    public async Task CapturePhoto_StoresImageReference_AndCountsAsEvidence()
    {
        var (db, tripId, wpId, _) = await SeedTripWithDeliveryWaypointAsync("pod_photo_" + Guid.NewGuid());
        var svc = new ProofOfDeliveryService(db);

        var record = await svc.CapturePhotoAsync(tripId, wpId, "driver-1", "https://cdn.example/pod/photo-1.jpg", 23.0, 72.0);

        Assert.Equal(ProofOfDeliveryType.Photo, record.Type);
        Assert.Equal("https://cdn.example/pod/photo-1.jpg", record.ImageUrl);
        Assert.True(await svc.HasVerifiedEvidenceAsync(tripId, wpId));
    }

    [Fact]
    public async Task SendOtp_StoresOnlyHash_NeverPlaintext()
    {
        var (db, tripId, wpId, _) = await SeedTripWithDeliveryWaypointAsync("pod_otp_hash_" + Guid.NewGuid());
        var svc = new ProofOfDeliveryService(db);

        var (record, otp) = await svc.SendOtpAsync(tripId, wpId, "driver-1");

        Assert.Equal(ProofOfDeliveryType.OtpCode, record.Type);
        Assert.Equal(6, otp.Length);
        Assert.False(string.IsNullOrEmpty(record.OtpCodeHash));
        Assert.NotEqual(otp, record.OtpCodeHash); // never stored in plaintext
        Assert.DoesNotContain(otp, record.OtpCodeHash!);
        Assert.False(await svc.HasVerifiedEvidenceAsync(tripId, wpId)); // pending until verified
    }

    [Fact]
    public async Task VerifyOtp_CorrectCode_StampsVerification_AndCountsAsEvidence()
    {
        var (db, tripId, wpId, _) = await SeedTripWithDeliveryWaypointAsync("pod_otp_ok_" + Guid.NewGuid());
        var svc = new ProofOfDeliveryService(db);
        var (record, otp) = await svc.SendOtpAsync(tripId, wpId, "driver-1");

        var result = await svc.VerifyOtpAsync(tripId, wpId, "driver-1", otp, 23.0, 72.0);

        Assert.True(result.Ok);
        Assert.Null(result.Error);
        Assert.True(result.Record!.OtpVerifiedAt.HasValue);
        Assert.True(await svc.HasVerifiedEvidenceAsync(tripId, wpId));
    }

    [Fact]
    public async Task VerifyOtp_WrongCode_IsRejected_AndDoesNotVerify()
    {
        var (db, tripId, wpId, _) = await SeedTripWithDeliveryWaypointAsync("pod_otp_wrong_" + Guid.NewGuid());
        var svc = new ProofOfDeliveryService(db);
        var (_, otp) = await svc.SendOtpAsync(tripId, wpId, "driver-1");
        var wrong = otp == "000000" ? "111111" : "000000";

        var result = await svc.VerifyOtpAsync(tripId, wpId, "driver-1", wrong, 23.0, 72.0);

        Assert.False(result.Ok);
        Assert.Contains("Incorrect", result.Error);
        Assert.False(await svc.HasVerifiedEvidenceAsync(tripId, wpId));
    }

    [Fact]
    public async Task VerifyOtp_ExpiredCode_IsRejected()
    {
        var (db, tripId, wpId, _) = await SeedTripWithDeliveryWaypointAsync("pod_otp_exp_" + Guid.NewGuid());
        var svc = new ProofOfDeliveryService(db);
        var (record, otp) = await svc.SendOtpAsync(tripId, wpId, "driver-1");
        record.CapturedAt = DateTime.UtcNow.AddMinutes(-11); // beyond 10-min lifetime
        await db.SaveChangesAsync();

        var result = await svc.VerifyOtpAsync(tripId, wpId, "driver-1", otp, 23.0, 72.0);

        Assert.False(result.Ok);
        Assert.Contains("expired", result.Error);
    }

    [Fact]
    public async Task VerifyOtp_WithoutIssuedOtp_IsRejected()
    {
        var (db, tripId, wpId, _) = await SeedTripWithDeliveryWaypointAsync("pod_otp_none_" + Guid.NewGuid());
        var svc = new ProofOfDeliveryService(db);

        var result = await svc.VerifyOtpAsync(tripId, wpId, "driver-1", "123456", 23.0, 72.0);

        Assert.False(result.Ok);
        Assert.Contains("No OTP", result.Error);
    }

    [Fact]
    public async Task RecaptureSignature_SoftDeletesPreviousEvidence()
    {
        var (db, tripId, wpId, _) = await SeedTripWithDeliveryWaypointAsync("pod_recap_" + Guid.NewGuid());
        var svc = new ProofOfDeliveryService(db);
        await svc.CaptureSignatureAsync(tripId, wpId, "driver-1", "<svg>first</svg>", 23.0, 72.0);

        var second = await svc.CaptureSignatureAsync(tripId, wpId, "driver-1", "<svg>second</svg>", 23.0, 72.0);

        var active = await svc.ListForWaypointAsync(tripId, wpId);
        Assert.Single(active);
        Assert.Equal(second.Id, active[0].Id);
        Assert.Equal("<svg>second</svg>", active[0].SignatureSvg);
        // The global !IsDeleted query filter hides the old row from normal queries —
        // count with the filter bypassed to prove the soft-delete actually happened.
        var all = await db.ProofOfDeliveries.IgnoreQueryFilters().Where(p => p.WaypointId == wpId).ToListAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal(1, all.Count(p => p.IsDeleted));
    }

    // ── Location mismatch (data-quality flag, never a block) ───────────────

    [Fact]
    public async Task Capture_FarFromWaypoint_FlagsLocationMismatch()
    {
        var (db, tripId, wpId, _) = await SeedTripWithDeliveryWaypointAsync("pod_mm_far_" + Guid.NewGuid(), wpLat: 23.0, wpLng: 72.0);
        var svc = new ProofOfDeliveryService(db);

        // ~0.5° south of the waypoint ≈ 55 km away
        var record = await svc.CaptureSignatureAsync(tripId, wpId, "driver-1", "<svg/>", 22.5, 72.0);

        Assert.True(record.LocationMismatch);
        Assert.Contains("km from the waypoint", record.LocationMismatchDetail);
    }

    [Fact]
    public async Task Capture_NearWaypoint_IsNotFlagged()
    {
        var (db, tripId, wpId, _) = await SeedTripWithDeliveryWaypointAsync("pod_mm_near_" + Guid.NewGuid(), wpLat: 23.0, wpLng: 72.0);
        var svc = new ProofOfDeliveryService(db);

        // ~0.002° ≈ 220 m — inside the 500 m tolerance
        var record = await svc.CapturePhotoAsync(tripId, wpId, "driver-1", "img://x", 23.002, 72.0);

        Assert.False(record.LocationMismatch);
        Assert.Contains("within", record.LocationMismatchDetail);
    }

    [Fact]
    public async Task Capture_WithoutCoordinates_IsUnknownNotFlagged()
    {
        var (db, tripId, wpId, _) = await SeedTripWithDeliveryWaypointAsync("pod_mm_none_" + Guid.NewGuid());
        var svc = new ProofOfDeliveryService(db);

        var record = await svc.CaptureSignatureAsync(tripId, wpId, "driver-1", "<svg/>", null, null);

        Assert.Null(record.LocationMismatch);
        Assert.Contains("no capture location", record.LocationMismatchDetail);
    }

    [Fact]
    public async Task Capture_OutsideLinkedGeofence_FlagsAgainstGeofenceName()
    {
        var (db, tripId, wpId, company) = await SeedTripWithDeliveryWaypointAsync("pod_mm_fence_" + Guid.NewGuid(), wpLat: 23.0, wpLng: 72.0);
        var fence = new Geofence
        {
            Id = Guid.NewGuid(), Name = "Customer Yard", Type = GeofenceType.Circle, CompanyId = company,
            Geometry = "{\"type\":\"circle\",\"center\":[72.0,23.0],\"radiusMeters\":300}"
        };
        db.Geofences.Add(fence);
        var wp = await db.TripWaypoints.FirstAsync(w => w.Id == wpId);
        wp.LinkedGeofenceId = fence.Id;
        await db.SaveChangesAsync();
        var svc = new ProofOfDeliveryService(db);

        var outside = await svc.CaptureSignatureAsync(tripId, wpId, "driver-1", "<svg/>", 23.0, 72.1); // ~10 km east
        Assert.True(outside.LocationMismatch);
        Assert.Contains("Customer Yard", outside.LocationMismatchDetail);

        var inside = await svc.CapturePhotoAsync(tripId, wpId, "driver-1", "img://x", 23.0, 72.0);
        Assert.False(inside.LocationMismatch);
        Assert.Contains("Customer Yard", inside.LocationMismatchDetail);
    }

    // ── Arrival gate: company default + per-trip override ──────────────────

    private static async Task<TripLifecycleService.WaypointArrivalResult> ArriveAsync(ApplicationDbContext db, Guid tripId, Guid wpId)
    {
        var lifecycle = new TripLifecycleService(db, new AlwaysEntitledAlertEnforcement());
        return await lifecycle.RecordWaypointArrivalAsync(tripId, wpId);
    }

    [Fact]
    public async Task CompanyDefaultRequirePod_BlocksArrivalWithoutEvidence()
    {
        var (db, tripId, wpId, company) = await SeedTripWithDeliveryWaypointAsync("gate_cfg_true_" + Guid.NewGuid());
        await SetCompanyRequirePodAsync(db, company, true);

        var result = await ArriveAsync(db, tripId, wpId);

        Assert.True(result.Found);
        Assert.True(result.PodRequired);
        var wp = await db.TripWaypoints.FirstAsync(w => w.Id == wpId);
        Assert.Null(wp.ActualArrival); // gate does not change the waypoint
    }

    [Fact]
    public async Task PerTripOverrideFalse_OverridesCompanyDefault_AllowsArrival()
    {
        var (db, tripId, wpId, company) = await SeedTripWithDeliveryWaypointAsync("gate_override_" + Guid.NewGuid());
        await SetCompanyRequirePodAsync(db, company, true);
        var trip = await db.Trips.FirstAsync(t => t.Id == tripId);
        trip.RequirePodForDelivery = false;
        await db.SaveChangesAsync();

        var result = await ArriveAsync(db, tripId, wpId);

        Assert.True(result.Found);
        Assert.False(result.PodRequired);
        var wp = await db.TripWaypoints.FirstAsync(w => w.Id == wpId);
        Assert.NotNull(wp.ActualArrival);
    }

    [Fact]
    public async Task PerTripOverrideTrue_RequiresPod_EvenWithoutCompanyConfig()
    {
        var (db, tripId, wpId, _) = await SeedTripWithDeliveryWaypointAsync("gate_trip_true_" + Guid.NewGuid());
        var trip = await db.Trips.FirstAsync(t => t.Id == tripId);
        trip.RequirePodForDelivery = true;
        await db.SaveChangesAsync();

        var result = await ArriveAsync(db, tripId, wpId);

        Assert.True(result.PodRequired);
    }

    [Fact]
    public async Task NoPolicy_DefaultsToPodOptional_AllowsArrival()
    {
        var (db, tripId, wpId, _) = await SeedTripWithDeliveryWaypointAsync("gate_default_" + Guid.NewGuid());

        var result = await ArriveAsync(db, tripId, wpId);

        Assert.True(result.Found);
        Assert.False(result.PodRequired);
    }

    [Fact]
    public async Task PodCaptured_AllowsArrival_EvenWhenRequired()
    {
        var (db, tripId, wpId, company) = await SeedTripWithDeliveryWaypointAsync("gate_evidence_" + Guid.NewGuid());
        await SetCompanyRequirePodAsync(db, company, true);
        var pod = new ProofOfDeliveryService(db);
        await pod.CaptureSignatureAsync(tripId, wpId, "driver-1", "<svg>signed</svg>", 23.0, 72.0);

        var result = await ArriveAsync(db, tripId, wpId);

        Assert.True(result.Found);
        Assert.False(result.PodRequired);
        var wp = await db.TripWaypoints.FirstAsync(w => w.Id == wpId);
        Assert.NotNull(wp.ActualArrival);
    }

    [Fact]
    public async Task NonDeliveryWaypoint_IsNeverPODGated()
    {
        var (db, tripId, wpId, company) = await SeedTripWithDeliveryWaypointAsync("gate_pickup_" + Guid.NewGuid(), type: TripWaypointType.Pickup);
        await SetCompanyRequirePodAsync(db, company, true);

        var result = await ArriveAsync(db, tripId, wpId);

        Assert.True(result.Found);
        Assert.False(result.PodRequired);
    }
}