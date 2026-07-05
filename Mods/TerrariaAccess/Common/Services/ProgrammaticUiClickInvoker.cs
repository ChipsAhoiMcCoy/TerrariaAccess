#nullable enable
using Terraria.UI;

namespace TerrariaAccess.Common.Services;

/// <summary>
/// Invokes Terraria UI click handlers for accessibility-driven navigation without letting those
/// handlers emit duplicate native menu sounds. Callers should play explicit Terraria Access UI cues.
/// </summary>
internal static class ProgrammaticUiClickInvoker
{
    public static void LeftClick(UIElement element, UIMouseEvent clickEvent)
    {
        NativeSoundSuppression.RunSynchronous(() => element.LeftClick(clickEvent));
    }
}
