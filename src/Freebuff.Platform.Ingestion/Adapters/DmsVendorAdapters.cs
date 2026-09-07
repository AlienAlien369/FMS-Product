using System.Text.Json;
using Freebuff.Platform.Ingestion.Contracts;

namespace Freebuff.Platform.Ingestion.Adapters;

/// <summary>
/// Shared plumbing for the DMS-capable JSON vendor adapters (Pictor, iTriangle,
/// Streamax). All three transmit an IMEI identity plus a JSON fix, and each
/// carries driver-behavior events in its OWN vocabulary — the only
/// vendor-specific surface is <see cref="MapEvents"/>, which translates that
/// vocabulary onto the canonical DriverBehaviorCatalog codes. Position/time
/// field names are per-vendor overrides; everything else is mechanical.
/// </summary>
public abstract class ImeiJsonAdapterBase : IVendorAdapter
{
    public abstract string VendorCode { get; }
    public abstract string ProtocolType { get; }
    public abstract string PayloadFormat { get; }

    protected const string MandatoryImeiMsg = "payload requires a numeric string 'imei' (15-17 digits)";

    // Per-vendor JSON field names (all optional except imei).
    protected virtual string TimeField => "ts";
    protected virtual string LatField => "lat";
    protected virtual string LonField => "lon";
    protected virtual string SpeedField => "speed";

    /// <summary>Translate the vendor's behavior-event vocabulary → canonical NormalizedBehaviorEvents.</summary>
    protected abstract IReadOnlyList<NormalizedBehaviorEvent> MapEvents(JsonElement root);

    /// <summary>
    /// JSON field carrying the hardware speed-governor limit; null when the
    /// vendor doesn't report one (cameras: Pictor/iTriangle — speed is GPS only,
    /// so the governor limit stays null → "not supported" in the UI).
    /// </summary>
    protected virtual string? GovernorLimitField => null;

    public bool TryExtractDeviceId(byte[] frame, out DeviceIdentity identity)
    {
        identity = DeviceIdentity.None;
        try
        {
            using var doc = Parse(frame);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            return TryReadImei(doc.RootElement, out identity);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public bool Validate(byte[] frame, out string? error)
    {
        if (frame.Length == 0) { error = "empty payload"; return false; }
        try
        {
            using var doc = Parse(frame);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { error = "payload must be a JSON object"; return false; }
            if (!TryReadImei(doc.RootElement, out _)) { error = MandatoryImeiMsg; return false; }
            var hasLat = TryGetDouble(doc.RootElement, LatField, out _);
            var hasLon = TryGetDouble(doc.RootElement, LonField, out _);
            if (hasLat != hasLon) { error = $"{LatField} and {LonField} must both be present (or both absent)"; return false; }
            if (hasLat)
            {
                if (!TryGetDouble(doc.RootElement, LatField, out var lat) || lat is < -90 or > 90) { error = "lat out of range"; return false; }
                if (!TryGetDouble(doc.RootElement, LonField, out var lon) || lon is < -180 or > 180) { error = "lon out of range"; return false; }
            }
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"invalid JSON: {ex.Message}";
            return false;
        }
    }

    public ParseResult Parse(byte[] frame, DateTime receivedAtUtc)
    {
        if (!Validate(frame, out var error)) return new ParseRejected(error ?? "invalid payload");
        using var doc = Parse(frame);
        var root = doc.RootElement;

        if (!TryReadImei(root, out var identity)) return new ParseRejected(MandatoryImeiMsg);

        var telemetry = new NormalizedTelemetry
        {
            Device = identity,
            EventTimeUtc = GetDateTime(root, TimeField) ?? receivedAtUtc,
            Latitude = GetDouble(root, LatField),
            Longitude = GetDouble(root, LonField),
            SpeedKmh = GetDouble(root, SpeedField),
            SpeedGovernorLimitKmh = GovernorLimitField != null ? GetDouble(root, GovernorLimitField) : null,
            BehaviorEvents = MapEvents(root)
        };
        return new ParseOk(telemetry);
    }

    private bool TryReadImei(JsonElement root, out DeviceIdentity identity)
    {
        identity = DeviceIdentity.None;
        if (!TryGetString(root, "imei", out var value)) return false;
        if (!value.All(char.IsDigit) || value.Length is < 15 or > 17) return false;
        identity = new DeviceIdentity(VendorCode, "Imei", value);
        return true;
    }

    private static JsonDocument Parse(byte[] frame)
    {
        try { return JsonDocument.Parse(frame); }
        catch (JsonException) { return JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(frame)); }
    }

    protected static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String) return false;
        value = el.GetString() ?? string.Empty;
        return true;
    }

    protected static string? GetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String) return null;
        return el.GetString();
    }

    protected static bool TryGetDouble(JsonElement root, string name, out double value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out var el)) return false;
        if (el.ValueKind == JsonValueKind.Number) return el.TryGetDouble(out value);
        if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), out value)) return true;
        return false;
    }

    protected static double? GetDouble(JsonElement root, string name)
        => TryGetDouble(root, name, out var v) ? v : null;

    /// <summary>Number or numeric-string confidence (clamped to 0..1); defaults to 1 for discrete events.</summary>
    protected static double ReadConfidence(JsonElement root, string name)
    {
        var raw = GetDouble(root, name) ?? 1.0;
        return Math.Clamp(raw, 0, 1);
    }

    protected static DateTime? GetDateTime(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String) return null;
        return DateTime.TryParse(el.GetString(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var dt)
            ? dt : null;
    }
}

/// <summary>
/// Pictor DMS adapter. Mock-format spec (documented until the real binary spec
/// lands — the transport codec can swap later without touching this class):
/// <code>
/// { "imei": "860123456789012", "ts": "2026-09-06T08:00:00Z",
///   "lat": 23.1, "lon": 72.6, "speed": 54.2,
///   "dms": [ { "event": "hb", "conf": 0.93, "clip": "https://cdn.pictor.example/clips/1.mp4" } ] }
/// </code>
/// DMS event codes: hb=harsh braking, accel=harsh acceleration, corner=harsh
/// cornering, idle=excessive idling, drowsy=drowsiness, distracted=distraction,
/// phone=phone usage, sos=panic button.
/// </summary>
[VendorAdapter("pictor")]
public sealed class PictorDmsJsonAdapter : ImeiJsonAdapterBase
{
    public override string VendorCode => "pictor";
    public override string ProtocolType => "http";
    public override string PayloadFormat => "pictor-json-v1";

    private static readonly IReadOnlyDictionary<string, string> Codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["hb"] = "harsh_braking",
        ["accel"] = "harsh_acceleration",
        ["corner"] = "harsh_cornering",
        ["idle"] = "excessive_idling",
        ["drowsy"] = "drowsiness",
        ["distracted"] = "distraction",
        ["phone"] = "phone_usage",
        ["sos"] = "sos_triggered"
    };

    protected override IReadOnlyList<NormalizedBehaviorEvent> MapEvents(JsonElement root)
    {
        var result = new List<NormalizedBehaviorEvent>();
        if (!root.TryGetProperty("dms", out var arr) || arr.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var code = GetString(item, "event");
            if (code == null || !Codes.TryGetValue(code, out var canonical)) continue;
            result.Add(new NormalizedBehaviorEvent
            {
                EventType = canonical,
                Confidence = ReadConfidence(item, "conf"),
                MediaUrl = GetString(item, "clip")
            });
        }
        return result;
    }
}

/// <summary>
/// iTriangle DMS adapter. Mock-format spec:
/// <code>
/// { "imei": "860123456789012", "timestamp": "2026-09-06T08:01:00Z",
///   "latitude": 23.1, "longitude": 72.6, "speed": 41.0,
///   "events": [ { "type": "DROWSY", "confidence": 0.88 } ] }
/// </code>
/// Event codes: HARSH_BRAKE, RAPID_ACCEL, SHARP_TURN, IDLING, DROWSY,
/// DISTRACTED, PHONE_USE, PANIC.
/// </summary>
[VendorAdapter("itriangle")]
public sealed class ItriangleDmsJsonAdapter : ImeiJsonAdapterBase
{
    public override string VendorCode => "itriangle";
    public override string ProtocolType => "http";
    public override string PayloadFormat => "itriangle-json-v1";

    protected override string TimeField => "timestamp";
    protected override string LatField => "latitude";
    protected override string LonField => "longitude";
    protected override string SpeedField => "speed";

    private static readonly IReadOnlyDictionary<string, string> Codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["HARSH_BRAKE"] = "harsh_braking",
        ["RAPID_ACCEL"] = "harsh_acceleration",
        ["SHARP_TURN"] = "harsh_cornering",
        ["IDLING"] = "excessive_idling",
        ["DROWSY"] = "drowsiness",
        ["DISTRACTED"] = "distraction",
        ["PHONE_USE"] = "phone_usage",
        ["PANIC"] = "sos_triggered"
    };

    protected override IReadOnlyList<NormalizedBehaviorEvent> MapEvents(JsonElement root)
    {
        var result = new List<NormalizedBehaviorEvent>();
        if (!root.TryGetProperty("events", out var arr) || arr.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var code = GetString(item, "type");
            if (code == null || !Codes.TryGetValue(code, out var canonical)) continue;
            result.Add(new NormalizedBehaviorEvent
            {
                EventType = canonical,
                Confidence = ReadConfidence(item, "confidence"),
                MediaUrl = GetString(item, "mediaUrl")
            });
        }
        return result;
    }
}

/// <summary>
/// Streamax DMS adapter. Mock-format spec — Streamax hardware reports ADAS
/// alarms as numeric codes; this JSON shape mirrors that vocabulary:
/// <code>
/// { "imei": "860123456789012", "time": "2026-09-06T08:02:00Z",
///   "lat": 23.15, "lng": 72.65, "spd": 38.7,
///   "media": "https://cdn.streamax.example/snap/9.jpg",
///   "alarms": [ { "code": 1, "confidence": 0.91 }, { "code": 8 } ] }
/// </code>
/// Alarm codes: 1=drowsiness, 2=distraction, 3=phone_usage, 4=harsh_braking,
/// 5=harsh_acceleration, 6=harsh_cornering, 7=excessive_idling, 8=sos_triggered.
/// The root-level <c>media</c> snapshot applies to every alarm in the payload.
/// </summary>
[VendorAdapter("streamax")]
public sealed class StreamaxDmsJsonAdapter : ImeiJsonAdapterBase
{
    public override string VendorCode => "streamax";
    public override string ProtocolType => "http";
    public override string PayloadFormat => "streamax-json-v1";

    protected override string TimeField => "time";
    protected override string LatField => "lat";
    protected override string LonField => "lng";
    protected override string SpeedField => "spd";

    /// <summary>Streamax telematics reports the configured governor limit; TPMS is not part of the standard protocol.</summary>
    protected override string? GovernorLimitField => "governorLimit";

    private static readonly IReadOnlyDictionary<int, string> Codes = new Dictionary<int, string>
    {
        [1] = "drowsiness",
        [2] = "distraction",
        [3] = "phone_usage",
        [4] = "harsh_braking",
        [5] = "harsh_acceleration",
        [6] = "harsh_cornering",
        [7] = "excessive_idling",
        [8] = "sos_triggered"
    };

    protected override IReadOnlyList<NormalizedBehaviorEvent> MapEvents(JsonElement root)
    {
        var result = new List<NormalizedBehaviorEvent>();
        if (!root.TryGetProperty("alarms", out var arr) || arr.ValueKind != JsonValueKind.Array) return result;
        var media = GetString(root, "media");
        foreach (var item in arr.EnumerateArray())
        {
            int code;
            if (item.ValueKind == JsonValueKind.Number)
            {
                if (!item.TryGetInt32(out code)) continue;
            }
            else if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number)
            {
                if (!codeEl.TryGetInt32(out code)) continue;
            }
            else continue;

            if (!Codes.TryGetValue(code, out var canonical)) continue;
            result.Add(new NormalizedBehaviorEvent
            {
                EventType = canonical,
                Confidence = ReadConfidence(item, "confidence"),
                MediaUrl = media ?? (item.ValueKind == JsonValueKind.Object ? GetString(item, "mediaUrl") : null)
            });
        }
        return result;
    }
}