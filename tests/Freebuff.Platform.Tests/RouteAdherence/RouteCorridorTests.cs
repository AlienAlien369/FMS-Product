using Freebuff.Platform.Infrastructure.RouteAdherence;
using Xunit;

namespace Freebuff.Platform.Tests.RouteAdherence;

public class RouteCorridorTests
{
    // A straight east–west path along latitude 22.0 between lng 70.0 and 74.0.
    private static readonly List<(double Lng, double Lat)> Line = new()
    {
        (70.0, 22.0), (74.0, 22.0)
    };

    [Fact]
    public void Point_OnThePath_IsZeroMetresAway()
    {
        var d = RouteCorridor.DistanceMetersToPath(22.0, 72.0, Line);
        Assert.InRange(d, 0, 1);
    }

    [Theory]
    [InlineData(0.0005, 56)]   // ~55.7 m north of the line
    [InlineData(0.0045, 501)]  // ~500.9 m north
    [InlineData(0.0090, 1002)] // ~1.0 km north
    public void Point_OffsetNorthOfLine_DistanceMatchesArc(double deltaLat, double expectedM)
    {
        var d = RouteCorridor.DistanceMetersToPath(22.0 + deltaLat, 72.0, Line);
        Assert.InRange(d, expectedM * 0.97, expectedM * 1.03);
    }

    [Fact]
    public void Point_BeforeTheLineEnds_ClampsToNearestVertex()
    {
        // Beyond lng 74 the nearest point is the segment end at (74.0, 22.0):
        // 0.5° lat (~55.7 km) + 0.6° lng scaled by cos(22°) (~61.9 km).
        var d = RouteCorridor.DistanceMetersToPath(22.5, 74.6, Line);
        Assert.InRange(d, 82_500, 84_000);
    }

    [Fact]
    public void Corridor_InsideBuffer_IsNotOutside()
    {
        Assert.False(RouteCorridor.IsOutsideCorridor(22.004, 72.0, Line, 1000)); // ~445 m
    }

    [Fact]
    public void Corridor_BeyondBuffer_IsOutside()
    {
        Assert.True(RouteCorridor.IsOutsideCorridor(22.012, 72.0, Line, 1000)); // ~1.3 km
    }

    [Fact]
    public void Corridor_EmptyPath_IsAlwaysOutside()
    {
        Assert.True(RouteCorridor.IsOutsideCorridor(22.0, 72.0, new List<(double, double)>(), 10_000));
    }

    [Fact]
    public void MultiSegment_Polyline_UsesNearestSegment()
    {
        // L-shaped path: east along lat 22, then north along lng 73.
        var path = new List<(double Lng, double Lat)> { (70.0, 22.0), (73.0, 22.0), (73.0, 25.0) };
        // Point ~367 m east of the vertical segment's middle → nearest is that
        // segment (0.0036° lng scaled by cos(23.5°)).
        var d = RouteCorridor.DistanceMetersToPath(23.5, 73.0036, path);
        Assert.InRange(d, 355, 380);
    }

    [Fact]
    public void ParseLineString_ExtractsCoordinates()
    {
        var pts = RoutePath.ParseLineString(
            "{\"type\":\"LineString\",\"coordinates\":[[72.5,23.0],[72.6,23.1],[72.7,23.05]]}");
        Assert.Equal(3, pts.Count);
        Assert.Equal((72.5, 23.0), pts[0]);
        Assert.Equal((72.7, 23.05), pts[2]);
    }

    [Fact]
    public void ParseLineString_Garbage_ReturnsEmpty()
    {
        Assert.Empty(RoutePath.ParseLineString("not json"));
        Assert.Empty(RoutePath.ParseLineString(null));
        Assert.Empty(RoutePath.ParseLineString("{\"type\":\"Point\",\"coordinates\":[1,2]}"));
    }

    [Fact]
    public void FromWaypoints_ReadsLatLngPerStop()
    {
        var pts = RoutePath.FromWaypoints(
            "[{\"name\":\"A\",\"lat\":23.01,\"lng\":72.51,\"sequenceOrder\":0}," +
            "{\"name\":\"B\",\"lat\":23.02,\"lng\":72.52}]");
        Assert.Equal(2, pts.Count);
        Assert.Equal((72.51, 23.01), pts[0]);
        Assert.Equal((72.52, 23.02), pts[1]);
    }

    [Fact]
    public void FromWaypoints_SkipsRowsWithoutCoordinates()
    {
        var pts = RoutePath.FromWaypoints("[{\"name\":\"A\"},{\"lat\":1.0,\"lng\":2.0}]");
        Assert.Single(pts);
        Assert.Equal((2.0, 1.0), pts[0]);
    }
}
