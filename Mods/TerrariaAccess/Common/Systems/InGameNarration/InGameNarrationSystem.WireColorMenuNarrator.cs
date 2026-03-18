#nullable enable
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;
using Terraria.GameContent.UI;

namespace TerrariaAccess.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class WireColorMenuNarrator
    {
        private bool _wasOpen;
        private WiresUI.Settings.MultiToolMode _lastToolMode;

        public void Update()
        {
            // If the accessible wire color menu is open, skip processing entirely.
            // The accessible menu handles all its own announcements.
            if (AccessibleWireColorMenu.Instance.IsOpen)
            {
                // Keep tracking vanilla state so we don't get confused when accessible menu closes
                _wasOpen = false;
                _lastToolMode = WiresUI.Settings.ToolMode;
                return;
            }

            // Always keep _lastToolMode in sync to prevent false change detection
            // when menus open/close or when wire placement changes ToolMode
            WiresUI.Settings.MultiToolMode currentMode = WiresUI.Settings.ToolMode;

            // Handle vanilla menu (fallback if vanilla menu somehow opens without being intercepted)
            bool isOpen = WiresUI.Open;

            // Menu just opened
            if (isOpen && !_wasOpen)
            {
                _lastToolMode = currentMode;
                string openMessage = LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.WireColorMenu.Open",
                    "Wire Menu Opened");
                ScreenReaderService.Announce(openMessage, force: true);
                _wasOpen = true;
                return;
            }

            // Menu just closed - update tracking but don't announce changes
            if (!isOpen && _wasOpen)
            {
                _lastToolMode = currentMode;
                string closedMessage = LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.WireColorMenu.Closed",
                    "Wire menu closed");
                ScreenReaderService.Announce(closedMessage, force: true);
                _wasOpen = false;
                return;
            }

            // Menu is open - check for selection changes
            if (isOpen)
            {
                if (currentMode != _lastToolMode)
                {
                    AnnounceChanges(_lastToolMode, currentMode);
                    _lastToolMode = currentMode;
                }
            }
            else
            {
                // Menu is closed - silently track mode changes without announcing
                // This prevents spurious announcements when placing wires
                _lastToolMode = currentMode;
            }
        }

        private static void AnnounceChanges(WiresUI.Settings.MultiToolMode previous, WiresUI.Settings.MultiToolMode current)
        {
            CheckFlag(previous, current, WiresUI.Settings.MultiToolMode.Red, "Red", "RedOn", "RedOff");
            CheckFlag(previous, current, WiresUI.Settings.MultiToolMode.Green, "Green", "GreenOn", "GreenOff");
            CheckFlag(previous, current, WiresUI.Settings.MultiToolMode.Blue, "Blue", "BlueOn", "BlueOff");
            CheckFlag(previous, current, WiresUI.Settings.MultiToolMode.Yellow, "Yellow", "YellowOn", "YellowOff");
            CheckFlag(previous, current, WiresUI.Settings.MultiToolMode.Cutter, "Wire Cutter", "CutterOn", "CutterOff");
            CheckFlag(previous, current, WiresUI.Settings.MultiToolMode.Actuator, "Actuator", "ActuatorOn", "ActuatorOff");
        }

        private static void CheckFlag(
            WiresUI.Settings.MultiToolMode prev,
            WiresUI.Settings.MultiToolMode curr,
            WiresUI.Settings.MultiToolMode flag,
            string fallbackName,
            string onKey,
            string offKey)
        {
            bool wasSet = prev.HasFlag(flag);
            bool isSet = curr.HasFlag(flag);
            if (wasSet == isSet)
            {
                return;
            }

            string locKey = isSet
                ? $"Mods.TerrariaAccess.WireColorMenu.{onKey}"
                : $"Mods.TerrariaAccess.WireColorMenu.{offKey}";
            string fallback = isSet ? $"{fallbackName} on" : $"{fallbackName} off";
            string message = LocalizationHelper.GetTextOrFallback(locKey, fallback);
            ScreenReaderService.Announce(message, force: true);
        }
    }
}
