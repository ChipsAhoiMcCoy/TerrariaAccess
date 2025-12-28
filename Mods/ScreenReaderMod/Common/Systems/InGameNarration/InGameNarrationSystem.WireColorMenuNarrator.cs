#nullable enable
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Utilities;
using Terraria.GameContent.UI;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class WireColorMenuNarrator
    {
        private bool _wasOpen;
        private WiresUI.Settings.MultiToolMode _lastToolMode;

        public void Update()
        {
            bool isOpen = WiresUI.Open;

            // Menu just opened
            if (isOpen && !_wasOpen)
            {
                _lastToolMode = WiresUI.Settings.ToolMode;
                string openMessage = LocalizationHelper.GetTextOrFallback(
                    "Mods.ScreenReaderMod.WireColorMenu.Open",
                    "Wire Color Picker");
                ScreenReaderService.Announce(openMessage, force: true);
                _wasOpen = true;
                return;
            }

            // Menu just closed
            if (!isOpen && _wasOpen)
            {
                string closedMessage = LocalizationHelper.GetTextOrFallback(
                    "Mods.ScreenReaderMod.WireColorMenu.Closed",
                    "Wire menu closed");
                ScreenReaderService.Announce(closedMessage, force: true);
                _wasOpen = false;
                return;
            }

            // Menu is open - check for selection changes
            if (isOpen)
            {
                WiresUI.Settings.MultiToolMode currentMode = WiresUI.Settings.ToolMode;
                if (currentMode != _lastToolMode)
                {
                    AnnounceChanges(_lastToolMode, currentMode);
                    _lastToolMode = currentMode;
                }
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
                ? $"Mods.ScreenReaderMod.WireColorMenu.{onKey}"
                : $"Mods.ScreenReaderMod.WireColorMenu.{offKey}";
            string fallback = isSet ? $"{fallbackName} on" : $"{fallbackName} off";
            string message = LocalizationHelper.GetTextOrFallback(locKey, fallback);
            ScreenReaderService.Announce(message, force: true);
        }
    }
}
