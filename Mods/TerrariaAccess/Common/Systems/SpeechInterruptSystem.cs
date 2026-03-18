#nullable enable
using Microsoft.Xna.Framework;
using TerrariaAccess.Common.Services;
using Terraria;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Systems;

public sealed class SpeechInterruptSystem : ModSystem
{
    public override void UpdateUI(GameTime gameTime)
    {
        if (Main.dedServ)
        {
            return;
        }

        HandleInterrupt();
    }

    private static void HandleInterrupt()
    {
        if (SpeechInterruptKeybinds.Interrupt?.JustPressed ?? false)
        {
            ScreenReaderService.Interrupt();
            bool enabled = ScreenReaderService.ToggleSpeechInterrupt();
            string status = enabled ? "Speech interrupt enabled" : "Speech interrupt disabled";
            ScreenReaderService.Announce(status, force: true, allowWhenMuted: true);
        }
    }
}
