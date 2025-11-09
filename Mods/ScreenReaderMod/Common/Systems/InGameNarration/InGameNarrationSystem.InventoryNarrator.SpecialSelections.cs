#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
        private sealed partial class InventoryNarrator
        {
        private static readonly HashSet<int> LoggedUnknownInventoryPoints = new();
            private bool TryAnnounceSpecialSelection(bool hoverIsAir, string? location)
            {
                int currentPoint = UILinkPointNavigator.CurrentPoint;
            string? label = GetSpecialSelectionLabel(currentPoint, hoverIsAir, location);
            if (string.IsNullOrWhiteSpace(label))
            {
                return false;
            }

            if (string.Equals(_lastAnnouncedMessage, label, StringComparison.Ordinal))
            {
                return true;
            }

            _lastHover = ItemIdentity.Empty;
            _lastHoverTooltip = null;
            _lastHoverDetails = null;
            _lastHoverLocation = null;
            _lastEmptyMessage = null;
            _lastAnnouncedMessage = label;
            ScreenReaderService.Announce(label, force: true);
            return true;
        }

        private static string? GetSpecialSelectionLabel(int point, bool hoverIsAir, string? location)
        {
            static string? Button(string? text)
            {
                return string.IsNullOrWhiteSpace(text) ? null : $"{text} button";
            }

            string? result = point switch
            {
                301 => Button(Language.GetTextValue("GameUI.QuickStackToNearby")),
                302 => Button(Language.GetTextValue("GameUI.SortInventory")),
                304 => Button(Lang.inter[19].Value),
                305 => Button(Lang.inter[79].Value),
                306 => Button(Lang.inter[80].Value),
                307 => Button(Main.CaptureModeDisabled ? Lang.inter[115].Value : Lang.inter[81].Value),
                308 => Button(Lang.inter[62].Value),
                309 => Button(Language.GetTextValue("GameUI.Emote")),
                310 => Button(Language.GetTextValue("GameUI.Bestiary")),
                311 => Button(LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.InventorySpecial.LoadoutControls", "Loadout controls")),
                int loadout when loadout >= 312 && loadout <= 320 => Button(GetLoadoutLabel(loadout)),
                _ => null,
            };

            static string? GetLoadoutLabel(int point)
            {
                int index = point - 311;
                if (index < 1 || index > 9)
                {
                    return null;
                }

                return Language.GetTextValue($"UI.Loadout{index}");
            }

            if (!string.IsNullOrEmpty(result))
            {
                return result;
            }

            if (Main.ingameOptionsWindow)
            {
                return null;
            }

            LogUnknownInventoryPoint(point, hoverIsAir, location);
            return null;
        }

        private static int _lastOptionsStateHash = int.MinValue;

        private static void LogIngameOptionsState(int feature, bool hoverIsAir, string? location)
        {
            int leftHover = GetStaticFieldValue(IngameOptionsLeftHoverField);
            int category = GetStaticFieldValue(IngameOptionsCategoryField);
            int rightHover = IngameOptions.rightHover;
            int rightLock = IngameOptions.rightLock;
            int currentPoint = UILinkPointNavigator.CurrentPoint;
            int hash = HashCode.Combine(feature, leftHover, category, rightHover, rightLock, currentPoint, hoverIsAir ? 1 : 0, location ?? string.Empty);

            if (hash == _lastOptionsStateHash)
            {
                return;
            }

            _lastOptionsStateHash = hash;
            ScreenReaderMod.Instance?.Logger.Debug($"[IngameOptionsNarration] point={currentPoint} feature={feature} cat={category} left={leftHover} rightHover={rightHover} rightLock={rightLock} hoverIsAir={hoverIsAir} location='{location}'");
        }

        private static readonly FieldInfo? IngameOptionsLeftHoverField = typeof(IngameOptions).GetField("leftHover", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo? IngameOptionsCategoryField = typeof(IngameOptions).GetField("category", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private static int GetStaticFieldValue(FieldInfo? field)
        {
            if (field is null)
            {
                return -1;
            }

            try
            {
                object? value = field.GetValue(null);
                if (value is int intValue)
                {
                    return intValue;
                }
            }
            catch (Exception ex)
            {
                ScreenReaderMod.Instance?.Logger.Debug($"[IngameOptionsNarration] Unable to read {field.Name}: {ex.Message}");
            }

            return -1;
        }

        private static void LogUnknownInventoryPoint(int point, bool hoverIsAir, string? location)
        {
            if (!LoggedUnknownInventoryPoints.Add(point))
            {
                return;
            }

            string state = Main.InGameUI?.CurrentState?.GetType().FullName ?? "<null>";
            bool usingGamepad = PlayerInput.UsingGamepadUI;
            bool inventoryOpen = Main.playerInventory;
            ScreenReaderMod.Instance?.Logger.Info(
                $"[InventoryNarration] Unknown UI link point {point} (hoverIsAir={hoverIsAir}, location='{location ?? string.Empty}', usingGamepad={usingGamepad}, inventory={inventoryOpen}, state={state})");
        }
    }
}
