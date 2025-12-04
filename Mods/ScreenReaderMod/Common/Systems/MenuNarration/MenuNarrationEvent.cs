#nullable enable
namespace ScreenReaderMod.Common.Systems.MenuNarration;

internal enum MenuNarrationEventKind
{
    Unknown = 0,
    ModeChanged = 1,
    Hover = 2,
    Focus = 3,
    Slider = 4,
    WorldCreation = 5,
    ModConfig = 6,
    SpecialFeature = 7,
}

internal readonly record struct MenuNarrationEvent(string Text, bool Force, MenuNarrationEventKind Kind = MenuNarrationEventKind.Unknown);
