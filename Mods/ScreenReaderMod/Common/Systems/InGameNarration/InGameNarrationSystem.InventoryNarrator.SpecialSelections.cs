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
        private static readonly SpecialSelectionRepeatGuard SpecialSelectionRepeat = new();
            private bool TryAnnounceSpecialSelection(bool hoverIsAir, string? location)
            {
                int currentPoint = UILinkPointNavigator.CurrentPoint;
            string? label = GetSpecialSelectionLabel(currentPoint, hoverIsAir, location);
            if (string.IsNullOrWhiteSpace(label))
            {
                return false;
            }

            if (!SpecialSelectionRepeat.ShouldAnnounce(currentPoint))
            {
                return true;
            }

            PlayTickIfNew($"special-{currentPoint}");
            _currentFocus = null;
            _focusTracker.ClearSpecialLinkPoint(currentPoint);

            ResetHoverSlotsAndTooltips();
            _narrationHistory.Reset(NarrationKind.SpecialSelection);
            UiAreaNarrationContext.RecordArea(UiNarrationArea.Inventory);
            SpecialSelectionRepeat.Record(currentPoint);
            TryAnnounceCue(NarrationCue.ForSpecial(label), force: true);
            return true;
        }

        private static string? GetSpecialSelectionLabel(int point, bool hoverIsAir, string? location)
        {
            string? result = point switch
            {
                301 => FormatButtonLabel(Language.GetTextValue("GameUI.QuickStackToNearby")),
                302 => FormatButtonLabel(Language.GetTextValue("GameUI.SortInventory")),
                304 => FormatButtonLabel(Lang.inter[19].Value),
                305 => FormatButtonLabel(Lang.inter[79].Value),
                306 => FormatButtonLabel(Lang.inter[80].Value),
                307 => FormatButtonLabel(Main.CaptureModeDisabled ? Lang.inter[115].Value : Lang.inter[81].Value),
                308 => FormatButtonLabel(Lang.inter[62].Value),
                309 => FormatButtonLabel(Language.GetTextValue("GameUI.Emote")),
                310 => FormatButtonLabel(Language.GetTextValue("GameUI.Bestiary")),
                311 => FormatButtonLabel(LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.InventorySpecial.LoadoutControls", "Loadout controls")),
                int loadout when loadout >= 312 && loadout <= 320 => Button(GetLoadoutLabel(loadout)),
                int chestButton when chestButton >= 500 && chestButton <= 505 => DescribeChestButton(chestButton),
                1550 => Button(GetPvpToggleText()),
                int teamButton when teamButton >= 1551 && teamButton <= 1556 => Button(GetTeamButtonText(teamButton)),
                1557 => DescribeDefenseCounter(),
                _ => null,
            };

            static string? Button(string? text) => FormatButtonLabel(text);

            static string? FormatButtonLabel(string? text)
            {
                string cleaned = TextSanitizer.Clean(text ?? string.Empty);
                return string.IsNullOrWhiteSpace(cleaned) ? null : $"{cleaned} button";
            }

            static string? GetLoadoutLabel(int point)
            {
                int index = point - 311;
                if (index < 1 || index > 9)
                {
                    return null;
                }

                return Language.GetTextValue($"UI.Loadout{index}");
            }

            static string? DescribeChestButton(int point)
            {
                string? label = point switch
                {
                    500 => GetLegacyInterfaceText(29), // Loot All
                    501 => GetLegacyInterfaceText(30), // Deposit All
                    502 => GetLegacyInterfaceText(31), // Quick Stack
                    503 => GetLegacyInterfaceText(82), // Restock
                    504 => GetLegacyInterfaceText(61), // Rename
                    505 => GetLegacyInterfaceText(122), // Sort Items
                    _ => null,
                };

                if (string.IsNullOrWhiteSpace(label))
                {
                    return null;
                }

                ScreenReaderMod.Instance?.Logger.Debug($"[InventoryNarration] Chest button {point} -> {label}");
                return Button(label);
            }

            static string? GetLegacyInterfaceText(int index)
            {
                if (index < 0 || index >= Lang.inter.Length)
                {
                    return null;
                }

                string value = Lang.inter[index]?.Value ?? string.Empty;
                return string.IsNullOrWhiteSpace(value) ? null : TextSanitizer.Clean(value);
            }

            if (!string.IsNullOrEmpty(result))
            {
                return result;
            }

            if (!Main.ingameOptionsWindow && ShouldLogUnknownInventoryPoint(hoverIsAir, location))
            {
                LogUnknownInventoryPoint(point, hoverIsAir, location);
            }

            return null;
        }

        private static bool IsSpecialInventoryPoint(int point)
        {
            return point switch
            {
                301 or 302 or 304 or 305 or 306 or 307 or 308 or 309 or 310 or 311 => true,
                >= 312 and <= 320 => true,
                >= 500 and <= 505 => true,
                1550 => true,
                >= 1551 and <= 1556 => true,
                1557 => true,
                _ => false,
            };
        }

        private static string? GetPvpToggleText()
        {
            Player? player = Main.LocalPlayer;
            bool hostile = player?.hostile ?? false;
            string key = hostile
                ? "Mods.ScreenReaderMod.InventorySpecial.DisablePvp"
                : "Mods.ScreenReaderMod.InventorySpecial.EnablePvp";
            string fallback = hostile ? "Disable PvP" : "Enable PvP";
            return LocalizationHelper.GetTextOrFallback(key, fallback);
        }

        private static string? GetTeamButtonText(int point)
        {
            int teamIndex = point - 1551;
            string[] fallbacks =
            {
                "No team",
                "Red team",
                "Green team",
                "Blue team",
                "Yellow team",
                "Pink team",
            };

            if (teamIndex < 0 || teamIndex >= fallbacks.Length)
            {
                return null;
            }

            string key = teamIndex switch
            {
                0 => "Mods.ScreenReaderMod.InventorySpecial.TeamNeutral",
                1 => "Mods.ScreenReaderMod.InventorySpecial.TeamRed",
                2 => "Mods.ScreenReaderMod.InventorySpecial.TeamGreen",
                3 => "Mods.ScreenReaderMod.InventorySpecial.TeamBlue",
                4 => "Mods.ScreenReaderMod.InventorySpecial.TeamYellow",
                5 => "Mods.ScreenReaderMod.InventorySpecial.TeamPink",
                _ => string.Empty,
            };

            return LocalizationHelper.GetTextOrFallback(key, fallbacks[teamIndex]);
        }

        private static string? DescribeDefenseCounter()
        {
            Player? player = Main.LocalPlayer;
            int defense = player?.statDefense ?? 0;
            string label = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.InventorySpecial.Defense", "Defense");
            string cleaned = TextSanitizer.Clean(label);
            return $"{cleaned} {defense}";
        }

        private sealed class SpecialSelectionRepeatGuard
        {
            private int _lastPoint = -1;

            public bool ShouldAnnounce(int point)
            {
                return point >= 0 && point != _lastPoint;
            }

            public void Record(int point)
            {
                if (point < 0)
                {
                    Clear();
                    return;
                }

                _lastPoint = point;
            }

            public void Clear()
            {
                _lastPoint = -1;
            }
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

        private static bool ShouldLogUnknownInventoryPoint(bool hoverIsAir, string? location)
        {
            if (!hoverIsAir)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(location))
            {
                return false;
            }

            return true;
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
