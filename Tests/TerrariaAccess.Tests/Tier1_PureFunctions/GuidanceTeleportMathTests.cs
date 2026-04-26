using FluentAssertions;
using Microsoft.Xna.Framework;
using TerrariaAccess.Common.Systems.Guidance;

namespace TerrariaAccess.Tests.Tier1_PureFunctions;

public sealed class GuidanceTeleportMathTests
{
    [Fact]
    public void AlignTopLeftToAnchorBottom_PlacesPlayerBottomOnAnchor()
    {
        Vector2 anchorBottom = new(500f, 800f);

        Vector2 topLeft = GuidanceTeleportMath.AlignTopLeftToAnchorBottom(anchorBottom, playerWidth: 20f, playerHeight: 42f);

        topLeft.Should().Be(new Vector2(490f, 758f));
    }

    [Fact]
    public void AlignTopLeftByBottomDelta_MatchesVanillaPlayerTeleportFormula()
    {
        Vector2 teleporterTopLeft = new(100f, 200f);
        Vector2 teleporterBottom = new(110f, 242f);
        Vector2 targetBottom = new(510f, 842f);

        Vector2 destination = GuidanceTeleportMath.AlignTopLeftByBottomDelta(
            teleporterTopLeft,
            teleporterBottom,
            targetBottom);

        destination.Should().Be(new Vector2(500f, 800f));
    }

    [Fact]
    public void ClampTopLeftToWorld_ConstrainsPositionToPlayableBounds()
    {
        Vector2 clamped = GuidanceTeleportMath.ClampTopLeftToWorld(
            new Vector2(-200f, 20000f),
            playerWidth: 20f,
            playerHeight: 42f,
            maxTilesX: 100,
            maxTilesY: 100);

        clamped.Should().Be(new Vector2(16f, 1526f));
    }
}
