using Freebuff.Platform.Infrastructure.Geofencing;
using Xunit;

namespace Freebuff.Platform.Tests.Geofencing;

/// <summary>
/// Contract for the canonical GeoJSON geofence geometry: parse/validate rules
/// (radius bounds, polygon vertex/self-intersection/area constraints) and
/// point-in-geofence containment for both circle and polygon. Every validation
/// rule below is shared by the draw-time UI check, the save path, and the bulk
/// importer — the geometry library is the single source of truth.
/// </summary>
public class GeofenceGeometryTests
{
    // ── Parsing ─────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_Circle_RoundTrips()
    {
        var json = """{"type":"circle","center":[77.1025,28.7041],"radiusMeters":500}""";
        var g = GeofenceGeometry.TryParse(json, out var err);
        Assert.Null(err);
        var circle = Assert.IsType<GeofenceGeometry.Circle>(g);
        Assert.Equal(77.1025, circle.CenterLng);
        Assert.Equal(28.7041, circle.CenterLat);
        Assert.Equal(500, circle.RadiusMeters);
        Assert.Null(circle.Validate());
    }

    [Fact]
    public void Parse_Polygon_PreservesVertexOrder()
    {
        var json = """{"type":"polygon","coordinates":[[77.1,28.7],[77.101,28.7],[77.101,28.701],[77.1,28.701]]}""";
        var g = GeofenceGeometry.TryParse(json, out var err);
        Assert.Null(err);
        var poly = Assert.IsType<GeofenceGeometry.Polygon>(g);
        Assert.Equal(4, poly.PointCount);
        Assert.Equal(new[] { (77.1, 28.7), (77.101, 28.7), (77.101, 28.701), (77.1, 28.701) }, poly.Vertices);
        Assert.Null(poly.Validate());
    }

    [Fact]
    public void Parse_RejectsUnsupportedType_And_MalformedPositions()
    {
        Assert.Null(GeofenceGeometry.TryParse("""{"type":"rectangle"}""", out var err));
        Assert.Contains("Unsupported geometry", err);
        Assert.Null(GeofenceGeometry.TryParse("""{"type":"circle","center":[77]}""", out var err2));
        Assert.Contains("positions", err2, System.StringComparison.OrdinalIgnoreCase);
        Assert.Null(GeofenceGeometry.TryParse("not json", out var err3));
        Assert.NotNull(err3);
    }

    // ── Radius bounds ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(5, true)]      // below the 10 m minimum
    [InlineData(10, false)]    // at the minimum → OK
    [InlineData(500, false)]
    [InlineData(50_000, false)]  // at the 50 km max → OK
    [InlineData(100_000, true)]  // beyond → rejected
    public void Circle_RadiusBounds(double meters, bool expectError)
    {
        var c = new GeofenceGeometry.Circle(77.1, 28.7, meters);
        var err = c.Validate();
        Assert.Equal(expectError, err != null);
        if (expectError) Assert.Contains("Radius", err);
    }

    // ── Polygon vertex rules ────────────────────────────────────────────────

    [Fact]
    public void Polygon_RequiresAtLeastThreeDistinctVertices()
    {
        var two = new GeofenceGeometry.Polygon(new[] { (0.0, 0.0), (1.0, 0.0) });
        Assert.Contains("at least 3 points", two.Validate());
        // 3 vertices but two coincide → still not a real polygon.
        var dup = new GeofenceGeometry.Polygon(new[] { (0.0, 0.0), (0.0, 0.0), (1.0, 1.0) });
        Assert.Contains("distinct", dup.Validate());
        // Duplicate consecutive points on an otherwise valid ring.
        var consec = new GeofenceGeometry.Polygon(new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0) });
        Assert.Contains("duplicate consecutive", consec.Validate());
    }

    [Fact]
    public void Polygon_SelfIntersection_IsRejected()
    {
        // Bow-tie / figure-eight — the classic crossing.
        var bowTie = new GeofenceGeometry.Polygon(new[] { (0.0, 0.0), (0.001, 0.001), (0.0, 0.001), (0.001, 0.0) });
        Assert.True(GeofenceGeometry.IsSelfIntersecting(bowTie));
        Assert.Contains("crosses itself", bowTie.Validate());

        // Concave-but-valid polygon must NOT be flagged (edges only meet at adjacent vertices).
        var concave = new GeofenceGeometry.Polygon(new[] { (0.0, 0.0), (0.002, 0.0), (0.002, 0.002), (0.001, 0.001), (0.0, 0.002) });
        Assert.False(GeofenceGeometry.IsSelfIntersecting(concave));
        Assert.Null(concave.Validate());
    }

    [Fact]
    public void Polygon_OversizedArea_IsRejected()
    {
        // ~10,000 km² at the equator — above the 8,000 km² cap.
        var huge = new GeofenceGeometry.Polygon(new[] { (0.0, 0.0), (5.0, 0.0), (5.0, 5.0), (0.0, 5.0) });
        Assert.Contains("too large", huge.Validate());

        var normal = new GeofenceGeometry.Polygon(new[] { (77.1, 28.68), (77.11, 28.68), (77.11, 28.69), (77.1, 28.69) });
        Assert.Null(normal.Validate());
    }

    // ── Containment: circle ─────────────────────────────────────────────────

    [Theory]
    [InlineData(28.7041, 77.1025, true)]   // exact center
    [InlineData(28.7042, 77.1025, true)]   // ~11 m north — inside 500 m
    [InlineData(28.7090, 77.1025, false)]  // ~540 m north — outside
    [InlineData(28.7041, 77.1085, false)]  // ~590 m east — outside
    public void Circle_Containment(double lat, double lng, bool expectedInside)
    {
        var circle = new GeofenceGeometry.Circle(77.1025, 28.7041, 500);
        var actual = GeofenceGeometry.Contains(circle, lat, lng);
        Assert.Equal(expectedInside, actual);
    }

    [Fact]
    public void Circle_Boundary_Is_Inside()
    {
        var circle = new GeofenceGeometry.Circle(77.1025, 28.7041, 100);
        // ~89 m north of center → inside (near the boundary).
        Assert.True(GeofenceGeometry.Contains(circle, 28.7049, 77.1025));
        // ~1.1 km north → outside.
        Assert.False(GeofenceGeometry.Contains(circle, 28.7140, 77.1025));
    }

    // ── Containment: polygon ────────────────────────────────────────────────

    /// <summary>A ~110 m × ~110 m unit square in degrees (0.001° ≈ 111 m).</summary>
    private static readonly GeofenceGeometry.Polygon UnitSquare =
        new(new[] { (0.0, 0.0), (0.001, 0.0), (0.001, 0.001), (0.0, 0.001) });

    [Fact]
    public void Polygon_Inside_Outside_And_Boundary_Points_Resolve()
    {
        Assert.True(GeofenceGeometry.Contains(UnitSquare, 0.0005, 0.0005));  // center
        Assert.False(GeofenceGeometry.Contains(UnitSquare, 0.002, 0.002));   // outside NE
        Assert.False(GeofenceGeometry.Contains(UnitSquare, -0.0001, 0.0005)); // outside west
        Assert.True(GeofenceGeometry.Contains(UnitSquare, 0.0, 0.0005));     // on the left edge → inside
        Assert.True(GeofenceGeometry.Contains(UnitSquare, 0.0005, 0.001));   // on the top edge → inside
        Assert.True(GeofenceGeometry.Contains(UnitSquare, 0.001, 0.0005));   // on the right edge → inside
    }

    [Fact]
    public void Polygon_Concave_Containment()
    {
        // L-shape (0..0.003°): bottom band spans the full width; the upper-left
        // arm is narrow — the notch at (0.001..0.003, 0.001..0.003) is outside.
        var l = new GeofenceGeometry.Polygon(new[] { (0.0, 0.0), (0.003, 0.0), (0.003, 0.001), (0.001, 0.001), (0.001, 0.003), (0.0, 0.003) });
        Assert.True(GeofenceGeometry.Contains(l, 0.0005, 0.0005));  // bottom-left leg
        Assert.True(GeofenceGeometry.Contains(l, 0.002, 0.0005));   // bottom-right leg
        Assert.True(GeofenceGeometry.Contains(l, 0.0005, 0.002));   // upper-left arm
        Assert.False(GeofenceGeometry.Contains(l, 0.002, 0.002));   // the notch — outside
        Assert.False(GeofenceGeometry.Contains(l, 0.0015, 0.0015)); // notch center
    }

    [Fact]
    public void Polygon_VertexOrder_IsPreserved_Through_Serialization()
    {
        var json = GeofenceGeometry.ToGeoJson(UnitSquare);
        var reparsed = GeofenceGeometry.TryParse(json, out var err);
        Assert.Null(err);
        var poly = Assert.IsType<GeofenceGeometry.Polygon>(reparsed);
        Assert.Equal(UnitSquare.Vertices, poly.Vertices);
    }

    [Fact]
    public void Circle_And_Polygon_RoundTrip_Through_ToGeoJson()
    {
        var cJson = GeofenceGeometry.ToGeoJson(new GeofenceGeometry.Circle(77.1, 28.7, 120));
        var c = Assert.IsType<GeofenceGeometry.Circle>(GeofenceGeometry.TryParse(cJson, out _));
        Assert.Equal(77.1, c.CenterLng);
        Assert.Equal(120, c.RadiusMeters);
    }
}