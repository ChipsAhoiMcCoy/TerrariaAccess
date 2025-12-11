#nullable enable
using Terraria;

namespace ScreenReaderMod.Common.Services;

internal static class RuntimeContext
{
    internal static RuntimeContextSnapshot GetSnapshot()
    {
        bool isServer = Main.dedServ;
        bool inMenu = Main.gameMenu;
        bool inGameUi = Main.InGameUI?.CurrentState is not null || Main.ingameOptionsWindow;
        bool hasActivePlayer = !isServer && Main.LocalPlayer is { active: true };
        bool worldActive = hasActivePlayer && !inMenu;
        bool paused = Main.gamePaused || inMenu;

        return new RuntimeContextSnapshot(
            IsServer: isServer,
            InMenu: inMenu,
            InGameUiOpen: inGameUi,
            HasActivePlayer: hasActivePlayer,
            IsPaused: paused,
            WorldActive: worldActive);
    }
}

internal readonly record struct RuntimeContextSnapshot(
    bool IsServer,
    bool InMenu,
    bool InGameUiOpen,
    bool HasActivePlayer,
    bool IsPaused,
    bool WorldActive)
{
    public bool CanNarrateGameplay => WorldActive && !IsPaused && !InGameUiOpen;
}
