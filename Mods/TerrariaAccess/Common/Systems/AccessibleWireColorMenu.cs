#nullable enable
using System;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI;
using Terraria.GameInput;
using Terraria.ID;

namespace TerrariaAccess.Common.Systems;

/// <summary>
/// Accessible replacement for the vanilla floating radial wire color menu.
/// Provides keyboard-navigable menu with NVDA narration support.
/// </summary>
public sealed class AccessibleWireColorMenu
{
    public static AccessibleWireColorMenu Instance { get; } = new();

    private static readonly WireMenuOption[] AllOptions =
    {
        new("Red", WiresUI.Settings.MultiToolMode.Red, "RedOn", "RedOff"),
        new("Green", WiresUI.Settings.MultiToolMode.Green, "GreenOn", "GreenOff"),
        new("Blue", WiresUI.Settings.MultiToolMode.Blue, "BlueOn", "BlueOff"),
        new("Yellow", WiresUI.Settings.MultiToolMode.Yellow, "YellowOn", "YellowOff"),
        new("Wire Cutter", WiresUI.Settings.MultiToolMode.Cutter, "CutterOn", "CutterOff"),
        new("Actuator", WiresUI.Settings.MultiToolMode.Actuator, "ActuatorOn", "ActuatorOff"),
    };

    private bool _isOpen;
    private int _selectedIndex;
    private InputSnapshot? _inputSnapshot;

    public bool IsOpen => _isOpen;
    public int SelectedIndex => _selectedIndex;

    /// <summary>
    /// Returns 6 if actuator is available (WireKite), otherwise 5 (MulticolorWrench).
    /// </summary>
    public int OptionCount => ShowActuator ? 6 : 5;

    private static bool ShowActuator => WiresUI.Settings.DrawToolAllowActuators;

    private AccessibleWireColorMenu() { }

    public void Open()
    {
        if (_isOpen)
        {
            return;
        }

        _isOpen = true;
        _selectedIndex = 0;

        // Capture current input state before blocking
        _inputSnapshot = new InputSnapshot
        {
            BlockInput = Main.blockInput,
            WritingText = PlayerInput.WritingText,
            PlayerInventory = Main.playerInventory,
            EditSign = Main.editSign,
            EditChest = Main.editChest,
            DrawingPlayerChat = Main.drawingPlayerChat,
            InFancyUI = Main.inFancyUI,
            SmartCursorWantedMouse = Main.SmartCursorWanted_Mouse,
            SmartCursorWantedGamePad = Main.SmartCursorWanted_GamePad,
        };

        // Block game input while menu is open
        Main.blockInput = true;
        PlayerInput.WritingText = true;

        // Play menu open sound
        SoundEngine.PlaySound(SoundID.MenuOpen);

        // Announce menu open bundled with first selection to avoid interruption
        AnnounceCurrentSelection(includeMenuOpenPrefix: true);
    }

    public void Close()
    {
        if (!_isOpen)
        {
            return;
        }

        _isOpen = false;

        // Notify the system to start cooldown (prevents immediate reopen)
        WireColorMenuSystem.NotifyMenuClosed();

        // Restore input state
        if (_inputSnapshot is InputSnapshot snapshot)
        {
            Main.blockInput = snapshot.BlockInput;
            PlayerInput.WritingText = snapshot.WritingText;
            Main.playerInventory = snapshot.PlayerInventory;
            Main.editSign = snapshot.EditSign;
            Main.editChest = snapshot.EditChest;
            Main.drawingPlayerChat = snapshot.DrawingPlayerChat;
            Main.inFancyUI = snapshot.InFancyUI;
            Main.SmartCursorWanted_Mouse = snapshot.SmartCursorWantedMouse;
            Main.SmartCursorWanted_GamePad = snapshot.SmartCursorWantedGamePad;
        }
        else
        {
            Main.blockInput = false;
            PlayerInput.WritingText = false;
        }

        _inputSnapshot = null;

        // Play menu close sound
        SoundEngine.PlaySound(SoundID.MenuClose);

        string closedMessage = LocalizationHelper.GetTextOrFallback(
            "Mods.TerrariaAccess.WireColorMenu.Closed",
            "Wire menu closed");
        ScreenReaderService.Announce(closedMessage, force: true);
    }

    public void NavigateUp()
    {
        if (!_isOpen)
        {
            return;
        }

        int count = OptionCount;
        _selectedIndex = (_selectedIndex - 1 + count) % count;
        SoundEngine.PlaySound(SoundID.MenuTick);
        AnnounceCurrentSelection();
    }

    public void NavigateDown()
    {
        if (!_isOpen)
        {
            return;
        }

        _selectedIndex = (_selectedIndex + 1) % OptionCount;
        SoundEngine.PlaySound(SoundID.MenuTick);
        AnnounceCurrentSelection();
    }

    public void ToggleSelected()
    {
        if (!_isOpen || _selectedIndex < 0 || _selectedIndex >= OptionCount)
        {
            return;
        }

        WireMenuOption option = AllOptions[_selectedIndex];
        WiresUI.Settings.MultiToolMode currentMode = WiresUI.Settings.ToolMode;
        bool wasEnabled = currentMode.HasFlag(option.Flag);

        // Toggle the flag
        if (wasEnabled)
        {
            WiresUI.Settings.ToolMode = currentMode & ~option.Flag;
        }
        else
        {
            WiresUI.Settings.ToolMode = currentMode | option.Flag;
        }

        bool isNowEnabled = !wasEnabled;
        SoundEngine.PlaySound(SoundID.MenuTick);

        // Announce toggle change
        string locKey = isNowEnabled
            ? $"Mods.TerrariaAccess.WireColorMenu.{option.OnKey}"
            : $"Mods.TerrariaAccess.WireColorMenu.{option.OffKey}";
        string fallback = isNowEnabled ? $"{option.Label} on" : $"{option.Label} off";
        string message = LocalizationHelper.GetTextOrFallback(locKey, fallback);
        ScreenReaderService.Announce(message, force: true);
    }

    public WireMenuOption GetOption(int index)
    {
        if (index < 0 || index >= AllOptions.Length)
        {
            return AllOptions[0];
        }

        return AllOptions[index];
    }

    public bool IsOptionEnabled(int index)
    {
        if (index < 0 || index >= AllOptions.Length)
        {
            return false;
        }

        return WiresUI.Settings.ToolMode.HasFlag(AllOptions[index].Flag);
    }

    /// <summary>
    /// Force-closes the menu without announcements. Used for edge cases
    /// like player death or world unload.
    /// </summary>
    public void ForceClose()
    {
        if (!_isOpen)
        {
            return;
        }

        _isOpen = false;

        // Notify the system to start cooldown (prevents immediate reopen)
        WireColorMenuSystem.NotifyMenuClosed();

        if (_inputSnapshot is InputSnapshot snapshot)
        {
            Main.blockInput = snapshot.BlockInput;
            PlayerInput.WritingText = snapshot.WritingText;
            Main.playerInventory = snapshot.PlayerInventory;
            Main.editSign = snapshot.EditSign;
            Main.editChest = snapshot.EditChest;
            Main.drawingPlayerChat = snapshot.DrawingPlayerChat;
            Main.inFancyUI = snapshot.InFancyUI;
            Main.SmartCursorWanted_Mouse = snapshot.SmartCursorWantedMouse;
            Main.SmartCursorWanted_GamePad = snapshot.SmartCursorWantedGamePad;
        }
        else
        {
            Main.blockInput = false;
            PlayerInput.WritingText = false;
        }

        _inputSnapshot = null;
    }

    /// <summary>
    /// Called each frame. Vanilla radial suppression is now handled by WireColorMenuSystem hook.
    /// </summary>
    public void Update()
    {
        // Vanilla radial menu suppression is handled by WireColorMenuSystem hook
        // which intercepts WiresUI.HandleWiresUI during the Draw cycle.
    }

    private void AnnounceCurrentSelection(bool includeMenuOpenPrefix = false)
    {
        if (_selectedIndex < 0 || _selectedIndex >= OptionCount)
        {
            return;
        }

        WireMenuOption option = AllOptions[_selectedIndex];
        bool enabled = IsOptionEnabled(_selectedIndex);
        string state = enabled ? "on" : "off";
        int position = _selectedIndex + 1;
        int total = OptionCount;

        string formatKey = "Mods.TerrariaAccess.WireColorMenu.OptionFormat";
        string fallback = $"{option.Label}, {state}, {position} of {total}";

        // Try to use localized format string if available
        string format = LocalizationHelper.GetTextOrFallback(formatKey, "{0}, {1}, {2} of {3}");
        string selectionAnnouncement;
        try
        {
            selectionAnnouncement = string.Format(format, option.Label, state, position, total);
        }
        catch
        {
            selectionAnnouncement = fallback;
        }

        // Bundle menu open message with first selection to avoid speech interruption
        string announcement;
        if (includeMenuOpenPrefix)
        {
            string menuOpenPrefix = LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.WireColorMenu.MenuOpenPrefix",
                "Wire Menu Opened");
            announcement = $"{menuOpenPrefix}. {selectionAnnouncement}";
        }
        else
        {
            announcement = selectionAnnouncement;
        }

        ScreenReaderService.Announce(announcement, force: true);
    }

    private readonly struct InputSnapshot
    {
        public bool BlockInput { get; init; }
        public bool WritingText { get; init; }
        public bool PlayerInventory { get; init; }
        public bool EditSign { get; init; }
        public bool EditChest { get; init; }
        public bool DrawingPlayerChat { get; init; }
        public bool InFancyUI { get; init; }
        public bool SmartCursorWantedMouse { get; init; }
        public bool SmartCursorWantedGamePad { get; init; }
    }
}

/// <summary>
/// Represents a single option in the wire color menu.
/// </summary>
public readonly record struct WireMenuOption(
    string Label,
    WiresUI.Settings.MultiToolMode Flag,
    string OnKey,
    string OffKey);
