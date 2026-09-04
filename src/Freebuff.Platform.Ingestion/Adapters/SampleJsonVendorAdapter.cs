using System.Text.Json;
using Freebuff.Platform.Ingestion.Contracts;

namespace Freebuff.Platform.Ingestion.Adapters;

/// <summary>
/// Sample JSON-webhook vendor — the reference adapter that proves the plug-in
/// pattern end-to-end. Payload (single JSON object, HTTP POST body):
/// <code>
/// { "imei": "860123456789012", "ts": "2026-09-04T10:00:00Z",
///   "lat": 28.6139, "lon": 77.2090, "speed": 42.5, "heading": 90,
///   "ignition": true, "engine": true, "fuelPercent": 65.0,
///   "odometerKm": 123456.0, "engineHours": 2345.5, "driverId": "D-123",
///   "alerts": ["overspeed"], "sensors": { "temp1": 24.5 } }
/// </code>
/// Only lat/lon/imei are mandatory; every other field is optional and null when
/// absent (not all vendors/devices send all fields).
/// </summary>
[VendorAdapter("sample-json")]
public sealed class SampleJsonVendorAdapter : IVendorAdapter
{
    public string VendorCode => "sample-json";
    public string ProtocolType => "http";
    public string PayloadFormat => "json-webhook";

    private const string MandatoryImeiLengthMsg = "payload requires a numeric string 'imei' (15-17 digits)";

    public bool TryExtractDeviceId(byte[] frame, out DeviceIdentity identity)
    {
        identity = DeviceIdentity.None;
        try
        {
            using var doc = Parse(frame);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!doc.RootElement.TryGetProperty("imei", out var imei) || imei.ValueKind != JsonValueKind.String)
                return false;
            var value = imei.GetString()!;
            if (!value.All(char.IsDigit) || value.Length is < 15 or > 17) return false;
            identity = new DeviceIdentity(VendorCode, "Imei", value);
            return true;
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
            if (!TryGetString(doc.RootElement, "imei", out var imei) || !imei.All(char.IsDigit) || imei.Length is < 15 or > 17)
            { error = MandatoryImeiLengthMsg; return false; }
            var hasLat = TryGetDouble(doc.RootElement, "lat", out _);
            var hasLon = TryGetDouble(doc.RootElement, "lon", out _);
            if (hasLat != hasLon) { error = "lat and lon must both be present (or both absent)"; return false; }
            if (hasLat)
            {
                if (!TryGetDouble(doc.RootElement, "lat", out var lat) || lat is < -90 or > 90) { error = "lat out of range"; return false; }
                if (!TryGetDouble(doc.RootElement, "lon", out var lon) || lon is < -180 or > 180) { error = "lon out of range"; return false; }
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

        var identity = ExtractImei(root);
        if (identity.IsEmpty) return new ParseRejected(MandatoryImeiLengthMsg);

        var sensors = new Dictionary<string, double>();
        if (root.TryGetProperty("sensors", out var sensorsEl) && sensorsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in sensorsEl.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out var d))
                    sensors[prop.Name] = d;
            }
        }

        var alerts = new List<string>();
        if (root.TryGetProperty("alerts", out var alertsEl) && alertsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in alertsEl.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String) alerts.Add(item.GetString()!);
        }

        // NOTE: use the nullable helpers — out-var locals default to 0 when a
        // field is absent, which would fabricate a position at (0,0).
        var lat = GetDouble(root, "lat");
        var lon = GetDouble(root, "lon");
        var speed = GetDouble(root, "speed");
        var heading = GetDouble(root, "heading");
        int? sats = TryGetInt(root, "satellites", out var satValue) ? satValue : null;

        var telemetry = new NormalizedTelemetry
        {
            Device = identity,
            EventTimeUtc = TryGetDateTime(root, "ts") ?? receivedAtUtc,
            Latitude = lat, Longitude = lon, SpeedKmh = speed, HeadingDeg = heading, Satellites = sats,
            AltitudeM = GetDouble(root, "alt"), Hdop = GetDouble(root, "hdop"),
            Ignition = GetBool(root, "ignition"), EngineOn = GetBool(root, "engine"),
            FuelLevelPercent = GetDouble(root, "fuelPercent"), FuelLevelLiters = GetDouble(root, "fuelLiters"),
            OdometerKm = GetDouble(root, "odometerKm"), EngineHours = GetDouble(root, "engineHours"),
            BatteryVoltage = GetDouble(root, "batteryVoltage"),
            DriverCardId = GetString(root, "driverId"),
            Alerts = alerts, Sensors = sensors
        };
        return new ParseOk(telemetry);
    }

    private static JsonDocument Parse(byte[] frame)
    {
        // Handles UTF-8 (with/without BOM) and falls back to UTF-16 for JSON ascii/hex.
        try { return JsonDocument.Parse(frame); }
        catch (JsonException) { return JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(frame)); }
    }

    private DeviceIdentity ExtractImei(JsonElement root)
    {
        if (!root.TryGetProperty("imei", out var imei) || imei.ValueKind != JsonValueKind.String) return DeviceIdentity.None;
        var value = imei.GetString()!;
        if (!value.All(char.IsDigit) || value.Length is < 15 or > 17) return DeviceIdentity.None;
        return new DeviceIdentity(VendorCode, "Imei", value);
    }

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String) return false;
        value = el.GetString() ?? string.Empty;
        return true;
    }

    private static string? GetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String) return null;
        return el.GetString();
    }

    private static bool TryGetDouble(JsonElement root, string name, out double value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out var el)) return false;
        if (el.ValueKind == JsonValueKind.Number) return el.TryGetDouble(out value);
        if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), out value)) return true;
        return false;
    }

    private static double? GetDouble(JsonElement root, string name)
        => TryGetDouble(root, name, out var v) ? v : null;

    private static bool TryGetInt(JsonElement root, string name, out int value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out var el)) return false;
        if (el.ValueKind == JsonValueKind.Number) return el.TryGetInt32(out value);
        return false;
    }

    private static bool? GetBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind == JsonValueKind.True) return true;
        if (el.ValueKind == JsonValueKind.False) return false;
        return null;
    }

    private static DateTime? TryGetDateTime(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String) return null;
        return DateTime.TryParse(el.GetString(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var dt)
            ? dt : null;
    }
}
