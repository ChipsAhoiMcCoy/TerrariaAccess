#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using TerrariaAccess.Common.Services;

namespace TerrariaAccess.Common.Systems;

internal sealed class NativeSoundSuppressionSystem : ModSystem
{
    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        On_Main.DrawMenu += RestoreAfterDrawMenu;
        On_Main.SaveSettings += RestoreBeforeSaveSettings;
    }

    public override void PostDrawInterface(SpriteBatch spriteBatch)
    {
        NativeSoundSuppression.RestoreDeferredSuppression();
    }

    public override void Unload()
    {
        if (!Main.dedServ)
        {
            On_Main.DrawMenu -= RestoreAfterDrawMenu;
            On_Main.SaveSettings -= RestoreBeforeSaveSettings;
        }

        NativeSoundSuppression.ResetState();
    }

    private static void RestoreAfterDrawMenu(On_Main.orig_DrawMenu orig, Main self, GameTime gameTime)
    {
        orig(self, gameTime);
        NativeSoundSuppression.RestoreDeferredSuppression();
    }

    private static bool RestoreBeforeSaveSettings(On_Main.orig_SaveSettings orig)
    {
        NativeSoundSuppression.ResetState();
        return orig();
    }
}
