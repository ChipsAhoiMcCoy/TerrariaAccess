#nullable enable
using System.Collections.Generic;
using System.Text;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.Journey;
using TerrariaAccess.Common.Utilities;
using Terraria;

namespace TerrariaAccess.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class JourneyPowersNarrator
    {
        private readonly Dictionary<string, bool> _lastToggleStates = new();
        private readonly Dictionary<string, int> _lastSliderQuantized = new();
        private bool _primed;

        public void Update(Player player)
        {
            if (player is null || !Main.CreativeMenu.Enabled)
            {
                if (_primed)
                {
                    _lastToggleStates.Clear();
                    _lastSliderQuantized.Clear();
                    _primed = false;
                }
                return;
            }

            bool primingPass = !_primed;

            foreach (JourneyPowerEntry entry in JourneyPowerRegistry.All)
            {
                object? power = JourneyPowersReflection.TryGetPower(entry.Key);
                if (power is null)
                {
                    continue;
                }

                switch (entry.Kind)
                {
                    case JourneyPowerKind.Toggle:
                    case JourneyPowerKind.Shared:
                    {
                        bool? state = JourneyPowersReflection.TryGetTogglePerPlayerState(power, player.whoAmI);
                        if (!state.HasValue)
                        {
                            continue;
                        }

                        if (_lastToggleStates.TryGetValue(entry.Key, out bool last) && last == state.Value)
                        {
                            continue;
                        }

                        _lastToggleStates[entry.Key] = state.Value;
                        if (!primingPass)
                        {
                            AnnounceToggle(entry, state.Value);
                        }
                        break;
                    }
                    case JourneyPowerKind.Slider:
                    {
                        float? value = JourneyPowersReflection.TryGetSliderValue(power, player.whoAmI);
                        if (!value.HasValue)
                        {
                            continue;
                        }

                        int quantized = JourneySliderValueFormatter.QuantizeForChangeDetection(entry.Key, value.Value);
                        if (_lastSliderQuantized.TryGetValue(entry.Key, out int last) && last == quantized)
                        {
                            continue;
                        }

                        _lastSliderQuantized[entry.Key] = quantized;
                        if (!primingPass)
                        {
                            AnnounceSlider(entry, value.Value);
                        }
                        break;
                    }
                }
            }

            _primed = true;
        }

        public string BuildCurrentStateSummary(Player player)
        {
            if (player is null || !Main.CreativeMenu.Enabled)
            {
                return string.Empty;
            }

            StringBuilder sb = new();
            foreach (JourneyPowerEntry entry in JourneyPowerRegistry.All)
            {
                object? power = JourneyPowersReflection.TryGetPower(entry.Key);
                if (power is null) continue;

                string label = ResolveLabel(entry);

                switch (entry.Kind)
                {
                    case JourneyPowerKind.Toggle:
                    case JourneyPowerKind.Shared:
                    {
                        bool? state = JourneyPowersReflection.TryGetTogglePerPlayerState(power, player.whoAmI);
                        if (!state.HasValue) continue;
                        string stateWord = state.Value
                            ? LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.JourneyMode.State.On", "on")
                            : LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.JourneyMode.State.Off", "off");
                        AppendListItem(sb, $"{label} {stateWord}");
                        break;
                    }
                    case JourneyPowerKind.Slider:
                    {
                        float? value = JourneyPowersReflection.TryGetSliderValue(power, player.whoAmI);
                        if (!value.HasValue) continue;
                        string formatted = JourneySliderValueFormatter.Format(entry.Key, value.Value);
                        AppendListItem(sb, $"{label} {formatted}");
                        break;
                    }
                }
            }

            return sb.ToString();
        }

        private static void AppendListItem(StringBuilder sb, string item)
        {
            if (sb.Length > 0)
            {
                sb.Append(", ");
            }

            sb.Append(item);
        }

        private static void AnnounceToggle(JourneyPowerEntry entry, bool state)
        {
            string label = ResolveLabel(entry);
            string formatKey = state
                ? "Mods.TerrariaAccess.JourneyMode.Powers.ToggleOnFormat"
                : "Mods.TerrariaAccess.JourneyMode.Powers.ToggleOffFormat";
            string fallbackFormat = state ? "{0} on" : "{0} off";
            string message = string.Format(
                LocalizationHelper.GetTextOrFallback(formatKey, fallbackFormat),
                label);
            ScreenReaderService.Announce(message, force: true);
        }

        private static void AnnounceSlider(JourneyPowerEntry entry, float value01)
        {
            string label = ResolveLabel(entry);
            string valueText = JourneySliderValueFormatter.Format(entry.Key, value01);
            string message = string.Format(
                LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.JourneyMode.Powers.SliderAnnouncementFormat",
                    "{0}: {1}"),
                label,
                valueText);
            ScreenReaderService.Announce(message, force: false);
        }

        private static string ResolveLabel(JourneyPowerEntry entry)
        {
            return LocalizationHelper.GetTextOrFallback(
                $"Mods.TerrariaAccess.JourneyMode.Power.{entry.LocSuffix}",
                entry.FallbackLabel);
        }
    }
}
