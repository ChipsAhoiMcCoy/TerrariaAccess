#nullable enable
using TerrariaAccess.Common.Systems.GamepadEmulation;

namespace TerrariaAccess.Common.Systems.MenuNarration.ModConfig;

/// <summary>
/// Tracks direct activation input for the custom mod config UI.
/// </summary>
internal sealed class ModConfigInputState
{
    private bool _inventorySelectWasPressed;

    public bool ActionJustPressed { get; private set; }

    public void Update()
    {
        bool inventorySelectPressed = GamepadEmulationKeybinds.InventorySelect is { } selectKeybind &&
                                      VirtualTriggerService.IsKeybindPressed(selectKeybind);
        bool inventorySelectJustPressed = inventorySelectPressed && !_inventorySelectWasPressed;
        _inventorySelectWasPressed = inventorySelectPressed;

        ActionJustPressed = inventorySelectJustPressed;
    }

    public void Reset()
    {
        _inventorySelectWasPressed = GamepadEmulationKeybinds.InventorySelect is { } selectKeybind &&
                                     VirtualTriggerService.IsKeybindPressed(selectKeybind);
        ActionJustPressed = false;
    }
}
