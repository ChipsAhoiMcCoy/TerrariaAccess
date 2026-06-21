#nullable enable
using Microsoft.Xna.Framework;

namespace TerrariaAccess.Common.Systems.Guidance;

internal static class GuidanceTeleportMath
{
    public static Vector2 AlignTopLeftToAnchorBottom(Vector2 anchorBottom, float playerWidth, float playerHeight)
    {
        return anchorBottom - new Vector2(playerWidth * 0.5f, playerHeight);
    }

    public static Vector2 AlignTopLeftByBottomDelta(Vector2 teleporterTopLeft, Vector2 teleporterBottom, Vector2 targetBottom)
    {
        return teleporterTopLeft + targetBottom - teleporterBottom;
    }

    public static Vector2 ClampTopLeftToWorld(Vector2 topLeft, float playerWidth, float playerHeight, int maxTilesX, int maxTilesY)
    {
        float minX = 16f;
        float minY = 16f;
        float maxX = (maxTilesX - 2) * 16f - playerWidth;
        float maxY = (maxTilesY - 2) * 16f - playerHeight;

        return new Vector2(
            MathHelper.Clamp(topLeft.X, minX, maxX),
            MathHelper.Clamp(topLeft.Y, minY, maxY));
    }
}
