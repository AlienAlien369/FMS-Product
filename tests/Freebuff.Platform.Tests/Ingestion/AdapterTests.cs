using System.Text;
using Freebuff.Platform.Ingestion.Adapters;
using Freebuff.Platform.Ingestion.Contracts;
using Freebuff.Platform.Ingestion.Registry;
using Xunit;

namespace Freebuff.Platform.Tests.Ingestion;

/// <summary>
/// Adapter-level tests: each vendor adapter normalizes sample payloads to the
/// common schema, and malformed/incomplete payloads fail GRACEFULLY — a vendor
/// defect or garbage frame must never throw, because one vendor's failure must
/// not crash ingestion for other vendors' devices.
/// </summary>
public class AdapterTests
{
    private static readonly DateTime Received = new(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);

    // ── Registry ──────────────────────────────────────────────

    [Fact]
    public void Registry_ListsAllBuiltInAdapters_ByVendorCode()
    {
        var registry = VendorAdapterRegistry.CreateBuiltIn();

        Assert.NotNull(registry.Get("sample-json"));
        Assert.NotNull(registry.Get("pictor"));
        Assert.NotNull(registry.Get("itriangle"));
        Assert.Null(registry.Get("no-such-vendor"));
        Assert.Null(registry.Get(""));

        var codes = registry.All.Select(a => a.VendorCode).OrderBy(c => c).ToArray();
        Assert.Equal(new[] { "itriangle", "pictor", "sample-json" }, codes);
    }

    [Fact]
    public void Registry_GroupsAdaptersByTransport()
    {
        var registry = VendorAdapterRegistry.CreateBuiltIn();
        var http = registry.ForTransport("http").Select(a => a.VendorCode).OrderBy(c => c).ToArray();
        Assert.Equal(new[] { "itriangle", "sample-json" }, http);
        Assert.Single(registry.ForTransport("tcp"));
        Assert.Empty(registry.ForTransport("mqtt"));
    }

    // ── Sample JSON adapter — happy path ──────────────────────

    [Fact]
    public void SampleJson_FullPayload_NormalizesToCommonSchema()
    {
        var adapter = new SampleJsonVendorAdapter();
        var payload = Encoding.UTF8.GetBytes(
            """
            {"imei":"860123456789012","ts":"2026-09-04T10:11:12Z","lat":28.6139,"lon":77.2090,"alt":216.5,
             "speed":42.5,"heading":90.0,"satellites":8,"hdop":1.2,"ignition":true,"engine":true,
             "fuelPercent":65.0,"fuelLiters":42.1,"odometerKm":123456.7,"engineHours":2345.5,
             "batteryVoltage":12.4,"driverId":"D-123","alerts":["overspeed","harsh-brake"],
             "sensors":{"temp1":24.5,"temp2":25.1}}
            """);

        var result = adapter.Parse(payload, Received);

        var ok = Assert.IsType<ParseOk>(result);
        var t = ok.Telemetry;
        Assert.Equal("sample-json", t.Device.VendorCode);
        Assert.Equal("Imei", t.Device.IdentityType);
        Assert.Equal("860123456789012", t.Device.IdentityValue);
        Assert.Equal(new DateTime(2026, 9, 4, 10, 11, 12, DateTimeKind.Utc), t.EventTimeUtc);
        Assert.Equal(28.6139, t.Latitude);
        Assert.Equal(77.2090, t.Longitude);
        Assert.Equal(216.5, t.AltitudeM);
        Assert.Equal(42.5, t.SpeedKmh);
        Assert.Equal(90.0, t.HeadingDeg);
        Assert.Equal(8, t.Satellites);
        Assert.Equal(1.2, t.Hdop);
        Assert.True(t.Ignition);
        Assert.True(t.EngineOn);
        Assert.Equal(65.0, t.FuelLevelPercent);
        Assert.Equal(42.1, t.FuelLevelLiters);
        Assert.Equal(123456.7, t.OdometerKm);
        Assert.Equal(2345.5, t.EngineHours);
        Assert.Equal(12.4, t.BatteryVoltage);
        Assert.Equal("D-123", t.DriverCardId);
        Assert.Equal(new[] { "overspeed", "harsh-brake" }, t.Alerts);
        Assert.Equal(24.5, t.Sensors["temp1"]);
        Assert.Equal(25.1, t.Sensors["temp2"]);
    }

    [Fact]
    public void SampleJson_MinimalPayload_DefaultsMissingFieldsToNull_AndUsesReceiveTime()
    {
        var adapter = new SampleJsonVendorAdapter();
        var payload = Encoding.UTF8.GetBytes("""{"imei":"860999999999999","lat":-33.8688,"lon":151.2093}""");

        var ok = Assert.IsType<ParseOk>(adapter.Parse(payload, Received));
        var t = ok.Telemetry;

        Assert.Equal("860999999999999", t.Device.IdentityValue);
        Assert.Equal(-33.8688, t.Latitude);
        Assert.Equal(151.2093, t.Longitude);
        Assert.Equal(Received, t.EventTimeUtc); // no device ts → receive time
        Assert.Null(t.SpeedKmh);
        Assert.Null(t.Ignition);
        Assert.Null(t.FuelLevelPercent);
        Assert.Empty(t.Alerts);
        Assert.Empty(t.Sensors);
    }

    [Fact]
    public void SampleJson_StringifiedNumbers_AreAccepted()
    {
        var adapter = new SampleJsonVendorAdapter();
        var payload = Encoding.UTF8.GetBytes("""{"imei":"860111111111111","lat":"10.5","lon":"20.5","speed":"5"}""");
        var ok = Assert.IsType<ParseOk>(adapter.Parse(payload, Received));
        Assert.Equal(10.5, ok.Telemetry.Latitude);
        Assert.Equal(5.0, ok.Telemetry.SpeedKmh);
    }

    // ── Sample JSON adapter — malformed / incomplete handling ──

    [Theory]
    [InlineData("")]                                          // empty
    [InlineData("not json at all")]                           // garbage text
    [InlineData("""{"lat":28.6,"lon":77.2}""")]               // missing imei
    [InlineData("""{"imei":"123","lat":28.6}""")]             // imei too short
    [InlineData("""{"imei":"860111111111111notnumeric"}""")]  // non-numeric imei
    public void SampleJson_MalformedPayload_RejectsGracefully(string json)
    {
        var adapter = new SampleJsonVendorAdapter();
        ParseResult result;
        try
        {
            result = adapter.Parse(Encoding.UTF8.GetBytes(json), Received);
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException($"SampleJson adapter threw on malformed input — must reject, not crash: {ex.Message}");
        }

        var rejected = Assert.IsType<ParseRejected>(result);
        Assert.False(string.IsNullOrWhiteSpace(rejected.Reason));
    }

    [Fact]
    public void SampleJson_PayloadWithoutPosition_StillParses()
    {
        // A device may send a state-only heartbeat with no GPS fix.
        var adapter = new SampleJsonVendorAdapter();
        var payload = Encoding.UTF8.GetBytes("""{"imei":"860111111111111","ignition":false}""");
        var ok = Assert.IsType<ParseOk>(adapter.Parse(payload, Received));
        Assert.Null(ok.Telemetry.Latitude);
        Assert.False(ok.Telemetry.Ignition);
    }

    [Fact]
    public void SampleJson_LatWithoutLon_IsRejected()
    {
        var adapter = new SampleJsonVendorAdapter();
        var result = adapter.Parse(Encoding.UTF8.GetBytes("""{"imei":"860111111111111","lat":28.6}"""), Received);
        var rejected = Assert.IsType<ParseRejected>(result);
        Assert.Contains("lat and lon", rejected.Reason);
    }

    [Fact]
    public void SampleJson_OutOfRangeCoordinates_AreRejected()
    {
        var adapter = new SampleJsonVendorAdapter();
        var result = adapter.Parse(Encoding.UTF8.GetBytes("""{"imei":"860111111111111","lat":95,"lon":77.2}"""), Received);
        Assert.IsType<ParseRejected>(result);
    }

    [Fact]
    public void SampleJson_ValidateAndExtract_MatchEachOther()
    {
        var adapter = new SampleJsonVendorAdapter();
        var good = Encoding.UTF8.GetBytes("""{"imei":"860123456789012","lat":1,"lon":2}""");
        var bad = Encoding.UTF8.GetBytes("""<binary>""");

        Assert.True(adapter.Validate(good, out _));
        Assert.False(adapter.Validate(bad, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));

        Assert.True(adapter.TryExtractDeviceId(good, out var identity));
        Assert.Equal("860123456789012", identity.IdentityValue);
        Assert.False(adapter.TryExtractDeviceId(bad, out var empty));
        Assert.True(empty.IsEmpty);
    }

    // ── Placeholder adapters — registered, graceful, marked ──

    [Fact]
    public void PlaceholderAdapters_AreRegistered_AndRejectGracefully()
    {
        foreach (var adapter in new IVendorAdapter[] { new PictorTcpPlaceholderAdapter(), new ItriangleHttpPlaceholderAdapter() })
        {
            Assert.False(adapter.Validate(new byte[] { 0x01, 0x02 }, out var error));
            Assert.Contains("PLACEHOLDER", error, StringComparison.OrdinalIgnoreCase);

            Assert.False(adapter.TryExtractDeviceId(new byte[] { 0x01, 0x02 }, out var identity));
            Assert.True(identity.IsEmpty);

            var result = adapter.Parse(new byte[] { 0x01, 0x02 }, Received);
            var rejected = Assert.IsType<ParseRejected>(result);
            Assert.Contains("PLACEHOLDER", rejected.Reason, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ── Vendor isolation ──────────────────────────────────────

    [Fact]
    public void GarbageFrame_NeverThrows_AcrossAllAdapters()
    {
        var registry = VendorAdapterRegistry.CreateBuiltIn();
        var garbageFrames = new[]
        {
            Array.Empty<byte>(),
            Encoding.UTF8.GetBytes("<html>device web page</html>"),
            new byte[] { 0x00, 0xFF, 0x7E, 0x01, 0xAA, 0xBB },
            Encoding.UTF8.GetBytes("[]") // wrong shape for sample vendor
        };

        foreach (var frame in garbageFrames)
        {
            foreach (var adapter in registry.All)
            {
                ParseResult result;
                try
                {
                    result = adapter.Parse(frame, Received);
                }
                catch (Exception ex)
                {
                    throw new Xunit.Sdk.XunitException($"Adapter {adapter.VendorCode} threw on garbage frame — a vendor defect must not crash the shared pipeline: {ex.Message}");
                }
                // Any graceful outcome is acceptable; throwing is the defect.
                Assert.True(result is ParseRejected or ParseOk or NeedsMoreData);
            }
        }
    }

    [Fact]
    public void OneVendorRejection_DoesNotAffectAnotherVendor()
    {
        var registry = VendorAdapterRegistry.CreateBuiltIn();
        // Pictor traffic (binary garbage for a sample-json-only pipeline) hitting
        // the sample vendor must reject cleanly; the sample vendor's own device
        // frames must still parse right after that failure.
        var pictorLike = new byte[] { 0x11, 0x22, 0x33, 0x44 };

        var sample = registry.Get("sample-json")!;
        Assert.IsType<ParseRejected>(sample.Parse(pictorLike, Received));

        var good = Encoding.UTF8.GetBytes("""{"imei":"860777777777777","lat":1,"lon":2}""");
        Assert.IsType<ParseOk>(sample.Parse(good, Received));
    }
}
