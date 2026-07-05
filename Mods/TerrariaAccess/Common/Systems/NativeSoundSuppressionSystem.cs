#nullable enable
using Microsoft.Xna.Framework.Graphics;
using Terraria.ModLoader;
using TerrariaAccess.Common.Services;

namespace TerrariaAccess.Common.Systems;

internal sealed class NativeSoundSuppressionSystem : ModSystem
{
    public override void PostDrawInterface(SpriteBatch spriteBatch)
    {
        NativeSoundSuppression.RestoreDeferredSuppression();
    }

    public override void Unload()
    {
        NativeSoundSuppression.ResetState();
    }
}
