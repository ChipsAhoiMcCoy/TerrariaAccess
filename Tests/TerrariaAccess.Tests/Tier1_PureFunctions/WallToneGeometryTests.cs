#nullable enable
using TerrariaAccess.Common.Systems.Audio;

namespace TerrariaAccess.Tests.Tier1_PureFunctions;

public sealed class WallToneGeometryTests
{
    private static readonly Rectangle PlayerHitbox = new(64, 64, 20, 42);

    [Fact]
    public void Scan_ReturnsLeftWallDistance()
    {
        WallToneScanResult result = Scan(new Point(3, 5));

        result.LeftDistanceTiles.Should().Be(1);
        result.Left.Should().Be(new WallToneContact(1, new Point(3, 5)));
        result.RightDistanceTiles.Should().Be(0);
        result.CeilingDistanceTiles.Should().Be(0);
    }

    [Fact]
    public void Scan_ReturnsRightWallDistance()
    {
        WallToneScanResult result = Scan(new Point(7, 5));

        result.LeftDistanceTiles.Should().Be(0);
        result.RightDistanceTiles.Should().Be(3);
        result.Right.Should().Be(new WallToneContact(3, new Point(7, 5)));
        result.CeilingDistanceTiles.Should().Be(0);
    }

    [Fact]
    public void Scan_ReturnsCeilingDistance()
    {
        WallToneScanResult result = Scan(new Point(4, 1));

        result.LeftDistanceTiles.Should().Be(0);
        result.RightDistanceTiles.Should().Be(0);
        result.CeilingDistanceTiles.Should().Be(3);
        result.Ceiling.Should().Be(new WallToneContact(3, new Point(4, 1)));
    }

    [Fact]
    public void Scan_UsesLocalCeilingDirectionForInvertedGravity()
    {
        WallToneScanResult result = WallToneGeometry.Scan(
            PlayerHitbox,
            gravityDirection: -1f,
            BlockingPredicate(new Point(4, 7)));

        result.CeilingDistanceTiles.Should().Be(2);
        result.Ceiling.Should().Be(new WallToneContact(2, new Point(4, 7)));
    }

    [Fact]
    public void Scan_ReportsBothSideWallsWhenLeftAndRightAreBlocking()
    {
        WallToneScanResult result = Scan(new Point(3, 5), new Point(5, 5));

        result.HasBothSideWalls.Should().BeTrue();
        result.LeftDistanceTiles.Should().Be(1);
        result.RightDistanceTiles.Should().Be(1);
    }

    [Fact]
    public void Scan_IgnoresTilesRejectedByBlockingPredicate()
    {
        WallToneScanResult result = WallToneGeometry.Scan(
            PlayerHitbox,
            gravityDirection: 1f,
            (x, y) => x == 6 && y == 5);

        result.RightDistanceTiles.Should().Be(2);
    }

    [Fact]
    public void Scan_DoesNotReportTilesBeyondMaxRange()
    {
        WallToneScanResult result = Scan(new Point(11, 5), new Point(4, -3));

        result.LeftDistanceTiles.Should().Be(0);
        result.RightDistanceTiles.Should().Be(0);
        result.CeilingDistanceTiles.Should().Be(0);
    }

    [Fact]
    public void Scan_SelectsBlockingTileNearestPlayerCenterAsSource()
    {
        WallToneScanResult result = Scan(new Point(7, 4), new Point(7, 6), new Point(7, 5));

        result.Right.Should().Be(new WallToneContact(3, new Point(7, 5)));
    }

    private static WallToneScanResult Scan(params Point[] blockingTiles)
    {
        return WallToneGeometry.Scan(
            PlayerHitbox,
            gravityDirection: 1f,
            BlockingPredicate(blockingTiles));
    }

    private static WallToneGeometry.BlockingTilePredicate BlockingPredicate(params Point[] blockingTiles)
    {
        HashSet<Point> blocking = new(blockingTiles);
        return (tileX, tileY) => blocking.Contains(new Point(tileX, tileY));
    }
}
