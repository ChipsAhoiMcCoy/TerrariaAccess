#nullable enable
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace ScreenReaderMod.Common.Systems;

public sealed class MenuNarrationSystem : ModSystem
{
    private MenuNarration.MenuNarrationController? _controller;

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        _controller = new MenuNarration.MenuNarrationController();
        On_Main.DrawMenu += HandleDrawMenu;
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        On_Main.DrawMenu -= HandleDrawMenu;
        _controller = null;
    }

    private void HandleDrawMenu(On_Main.orig_DrawMenu orig, Main self, GameTime gameTime)
    {
        orig(self, gameTime);
        _controller?.Process(self);
    }
}
