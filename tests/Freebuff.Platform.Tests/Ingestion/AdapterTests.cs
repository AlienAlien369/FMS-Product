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
        Assert.NotNull(registry.Get("streamax"));
        Assert.Null(registry.Get("no-such-vendor"));
        Assert.Null(registry.Get(""));

        var codes = registry.All.Select(a => a.VendorCode).OrderBy(c => c).ToArray();
        Assert.Equal(new[] { "itriangle", "pictor", "sample-json", "streamax" }, codes);
    }

    [Fact]
    public void Registry_GroupsAdaptersByTransport()
    {
        var registry = VendorAdapterRegistry.CreateBuiltIn();
        var http = registry.ForTransport("http").Select(a => a.VendorCode).OrderBy(c => c).ToArray();
        Assert.Equal(new[] { "itriangle", "pictor", "sample-json", "streamax" }, http);
        Assert.Empty(registry.ForTransport("tcp"));
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

    // ── Driver-behavior / DMS normalization ───────────────────

    [Fact]
    public void SampleJson_BehaviorEvents_NormalizeToCanonicalCodes()
    {
        var adapter = new SampleJsonVendorAdapter();
        var payload = Encoding.UTF8.GetBytes(
            """
            {"imei":"860123456789012","ts":"2026-09-06T08:00:00Z","lat":23.1,"lon":72.6,
             "behaviorEvents":[
               {"type":"harsh_braking","confidence":0.92,"mediaUrl":"https://cdn.example.com/clips/ab1.mp4"},
               {"type":"drowsiness","confidence":0.87},
               {"type":"sos_triggered"},
               {"type":"not_a_real_code"}
             ]}
            """);

        var ok = Assert.IsType<ParseOk>(adapter.Parse(payload, Received));
        var events = ok.Telemetry.BehaviorEvents;

        // Unknown canonical codes survive the adapter (the alert pipeline drops
        // them) — the reference adapter does no vocabulary translation.
        Assert.Equal(4, events.Count);
        Assert.Equal("harsh_braking", events[0].EventType);
        Assert.Equal(0.92, events[0].Confidence);
        Assert.Equal("https://cdn.example.com/clips/ab1.mp4", events[0].MediaUrl);
        Assert.Equal("drowsiness", events[1].EventType);
        Assert.Equal(0.87, events[1].Confidence);
        Assert.Null(events[1].MediaUrl);
        Assert.Equal("sos_triggered", events[2].EventType);
        Assert.Equal(1.0, events[2].Confidence); // discrete event → default confidence
    }

    /// <summary>Every canonical event type through the sample adapter — the full DriverBehaviorCatalog vocabulary.</summary>
    [Theory]
    [InlineData("harsh_braking")]
    [InlineData("harsh_acceleration")]
    [InlineData("harsh_cornering")]
    [InlineData("excessive_idling")]
    [InlineData("drowsiness")]
    [InlineData("distraction")]
    [InlineData("phone_usage")]
    [InlineData("sos_triggered")]
    public void SampleJson_EveryCanonicalEventType_IsCarriedThrough(string code)
    {
        var adapter = new SampleJsonVendorAdapter();
        var payload = Encoding.UTF8.GetBytes($"{{\"imei\":\"860123456789012\",\"behaviorEvents\":[{{\"type\":\"{code}\"}}]}}");
        var ok = Assert.IsType<ParseOk>(adapter.Parse(payload, Received));
        var ev = Assert.Single(ok.Telemetry.BehaviorEvents);
        Assert.Equal(code, ev.EventType);
    }

    [Fact]
    public void PictorDms_VendorEventCodes_MapToCanonical()
    {
        var adapter = new PictorDmsJsonAdapter();
        var payload = Encoding.UTF8.GetBytes(
            """
            {"imei":"860123456789012","ts":"2026-09-06T08:00:00Z","lat":23.1,"lon":72.6,"speed":54.2,
             "dms":[
               {"event":"hb","conf":0.93,"clip":"https://cdn.pictor.example/clips/1.mp4"},
               {"event":"drowsy","conf":0.81},
               {"event":"sos"},
               {"event":"no-such-code"}
             ]}
            """);

        var ok = Assert.IsType<ParseOk>(adapter.Parse(payload, Received));
        var events = ok.Telemetry.BehaviorEvents;

        Assert.Equal(3, events.Count); // vendor-only code dropped at the adapter
        Assert.Equal("harsh_braking", events[0].EventType);
        Assert.Equal(0.93, events[0].Confidence);
        Assert.Equal("https://cdn.pictor.example/clips/1.mp4", events[0].MediaUrl);
        Assert.Equal("drowsiness", events[1].EventType);
        Assert.Equal(0.81, events[1].Confidence);
        Assert.Equal("sos_triggered", events[2].EventType);
        Assert.Equal(23.1, ok.Telemetry.Latitude);
        Assert.Equal(54.2, ok.Telemetry.SpeedKmh);
    }

    [Fact]
    public void ItriangleDms_VendorEventCodes_MapToCanonical()
    {
        var adapter = new ItriangleDmsJsonAdapter();
        var payload = Encoding.UTF8.GetBytes(
            """
            {"imei":"860123456789012","timestamp":"2026-09-06T08:01:00Z","latitude":23.1,"longitude":72.6,"speed":41.0,
             "events":[
               {"type":"HARSH_BRAKE"},
               {"type":"SHARP_TURN","confidence":0.7,"mediaUrl":"https://cdn.itriangle.example/snap/2.jpg"},
               {"type":"PANIC","confidence":0.99}
             ]}
            """);

        var ok = Assert.IsType<ParseOk>(adapter.Parse(payload, Received));
        var events = ok.Telemetry.BehaviorEvents;

        Assert.Equal(3, events.Count);
        Assert.Equal("harsh_braking", events[0].EventType);
        Assert.Equal(1.0, events[0].Confidence);
        Assert.Equal("harsh_cornering", events[1].EventType);
        Assert.Equal(0.7, events[1].Confidence);
        Assert.Equal("https://cdn.itriangle.example/snap/2.jpg", events[1].MediaUrl);
        Assert.Equal("sos_triggered", events[2].EventType);
        Assert.Equal(23.1, ok.Telemetry.Latitude);
    }

    [Fact]
    public void StreamaxDms_NumericAlarmCodes_MapToCanonical()
    {
        var adapter = new StreamaxDmsJsonAdapter();
        var payload = Encoding.UTF8.GetBytes(
            """
            {"imei":"860123456789012","time":"2026-09-06T08:02:00Z","lat":23.15,"lng":72.65,"spd":38.7,
             "media":"https://cdn.streamax.example/snap/9.jpg",
             "alarms":[{"code":1,"confidence":0.91},{"code":8},{"code":99}]}
            """);

        var ok = Assert.IsType<ParseOk>(adapter.Parse(payload, Received));
        var events = ok.Telemetry.BehaviorEvents;

        Assert.Equal(2, events.Count); // alarm 99 unknown → dropped
        Assert.Equal("drowsiness", events[0].EventType);
        Assert.Equal(0.91, events[0].Confidence);
        Assert.Equal("https://cdn.streamax.example/snap/9.jpg", events[0].MediaUrl); // root media applies
        Assert.Equal("sos_triggered", events[1].EventType);
        Assert.Equal(1.0, events[1].Confidence);
        Assert.Equal("https://cdn.streamax.example/snap/9.jpg", events[1].MediaUrl);
        Assert.Equal(38.7, ok.Telemetry.SpeedKmh);
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
