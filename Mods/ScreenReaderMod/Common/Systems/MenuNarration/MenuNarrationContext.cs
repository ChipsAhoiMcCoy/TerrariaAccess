#nullable enable
using System;
using Terraria;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems.MenuNarration;

internal readonly record struct MenuNarrationContext(Main Main, UIState? UiState, int MenuMode, DateTime Timestamp)
{
    internal bool IsMenuActive => Main.gameMenu;
}
