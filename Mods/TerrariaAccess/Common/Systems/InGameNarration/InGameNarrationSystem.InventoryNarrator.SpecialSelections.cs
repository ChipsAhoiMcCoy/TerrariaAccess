#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using TerrariaAccess.Common.Systems.InGameNarration;
using TerrariaAccess.Common.Systems.Journey;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.GameContent.Creative;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.UI.Gamepad;

namespace TerrariaAccess.Common.Systems;

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

            string activationLabel = string.Empty;
            bool announceActivation = IsJourneyActivationJustPressed() &&
                TryGetJourneyOneShotActivationLabel(currentPoint, out activationLabel);
            string announcement = announceActivation ? activationLabel : label;
            string repeatKey = GetSpecialSelectionRepeatKey(currentPoint, announcement);

            if (!announceActivation && !SpecialSelectionRepeat.ShouldAnnounce(repeatKey))
            {
                return true;
            }

            // Determine region for special selections
            InventoryRegion currentRegion = ResolveRegionForSpecialPoint(currentPoint);
            string? regionPrefix = null;
            if (currentRegion != InventoryRegion.None && currentRegion != _lastAnnouncedRegion)
            {
                regionPrefix = GetRegionDisplayName(currentRegion);
                _lastAnnouncedRegion = currentRegion;
            }

            PlayTickIfNew(
                $"special-{currentPoint}",
                debugContext: $"source=special key=special-{currentPoint} linkPoint={currentPoint}",
                forceImmediate: true);
            _currentFocus = null;
            _focusTracker.ClearSpecialLinkPoint(currentPoint);

            ResetHoverSlotsAndTooltips();
            _narrationHistory.Reset(NarrationKind.SpecialSelection);
            UiAreaNarrationContext.RecordArea(IsJourneySpecialPoint(currentPoint) ? UiNarrationArea.Creative : UiNarrationArea.Inventory);
            SpecialSelectionRepeat.Record(repeatKey);
            TryAnnounceCue(NarrationCue.ForSpecial(announcement), force: true, regionPrefix: regionPrefix);
            return true;
        }

        private static string GetSpecialSelectionRepeatKey(int point, string announcement)
        {
            return GetJourneySpecialSelectionRepeatKey(point) ??
                point.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + announcement;
        }

        private static string? GetSpecialSelectionLabel(int point, bool hoverIsAir, string? location)
        {
            string? result = GetJourneySpecialSelectionLabel(point);
            if (!string.IsNullOrEmpty(result))
            {
                return result;
            }

            result = point switch
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
                311 => FormatButtonLabel(LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.InventorySpecial.LoadoutControls", "Loadout controls")),
                int loadout when loadout >= 312 && loadout <= 320 => Button(GetLoadoutLabel(loadout)),
                int chestButton when chestButton >= 500 && chestButton <= 505 => DescribeChestButton(chestButton),
                int builderToggle when builderToggle >= 6000 && builderToggle <= 6011 => DescribeBuilderAccessoryToggle(builderToggle),
                1550 => Button(GetPvpToggleText()),
                int teamButton when teamButton >= 1551 && teamButton <= 1556 => Button(GetTeamButtonText(teamButton)),
                1557 => DescribeDefenseCounter(),
                1570 => FormatButtonLabel(LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.InventorySpecial.AchievementAdvisor", "Achievement Advisor")),
                _ => null,
            };

            static string? Button(string? text) => FormatButtonLabel(text);

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

                TerrariaAccess.Instance?.Logger.Debug($"[InventoryNarration] Chest button {point} -> {label}");
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

            if (!Main.ingameOptionsWindow && ShouldLogUnknownInventoryPoint(point, hoverIsAir, location))
            {
                LogUnknownInventoryPoint(point, hoverIsAir, location);
            }

            return null;
        }

        internal static bool IsSpecialInventoryPoint(int point)
        {
            if (IsJourneySpecialPoint(point))
            {
                return true;
            }

            return point switch
            {
                301 or 302 or 304 or 305 or 306 or 307 or 308 or 309 or 310 or 311 => true,
                >= 312 and <= 320 => true,
                >= 500 and <= 505 => true,
                >= 6000 and <= 6011 => true,
                1550 => true,
                >= 1551 and <= 1556 => true,
                1557 => true,
                1570 => true, // Achievement Advisor
                _ => false,
            };
        }

        private static InventoryRegion ResolveRegionForSpecialPoint(int point)
        {
            if (IsJourneySpecialPoint(point))
            {
                return InventoryRegion.Creative;
            }

            return point switch
            {
                // Inventory management buttons (Quick Stack, Sort)
                301 or 302 => InventoryRegion.InventoryExtras,
                // Equipment page buttons and Camera Mode
                >= 304 and <= 308 => InventoryRegion.CharacterPanel,
                // Emote, Bestiary, and Loadout Controls buttons (visually near inventory)
                309 or 310 or 311 => InventoryRegion.InventoryExtras,
                // Individual loadout slots
                >= 312 and <= 320 => InventoryRegion.CharacterPanel,
                // Chest buttons
                >= 500 and <= 505 => InventoryRegion.Storage,
                // Builder accessory toggles
                >= 6000 and <= 6011 => InventoryRegion.InventoryExtras,
                // PvP and team buttons, defense counter
                >= 1550 and <= 1557 => InventoryRegion.CharacterPanel,
                // Achievement Advisor
                1570 => InventoryRegion.CharacterPanel,
                _ => InventoryRegion.None,
            };
        }

        private static bool IsJourneySpecialPoint(int point)
        {
            if (!Main.CreativeMenu.Enabled)
            {
                return false;
            }

            if (point == 15000)
            {
                return true;
            }

            if (point < 10000 || point > 11000)
            {
                return false;
            }

            if (point <= 10006)
            {
                return true;
            }

            int option = JourneyReflection.TryGetCurrentPowersCategoryOption() ?? 0;
            return option switch
            {
                1 => point >= 10007 && point <= 10018,
                2 => point == 10007,
                3 => point >= 10007 && point <= 10013,
                4 => point >= 10007 && point <= 10011,
                5 => point == 10007,
                6 => point >= 10007 && point <= 10010,
                _ => false,
            };
        }

        private static string? GetJourneySpecialSelectionLabel(int point)
        {
            if (!Main.CreativeMenu.Enabled)
            {
                return null;
            }

            if (point == 15000)
            {
                return DescribeJourneySacrificeSlot();
            }

            if (point < 10000 || point > 11000)
            {
                return null;
            }

            if (point <= 10006)
            {
                return DescribeJourneyMainStripPoint(point);
            }

            int option = JourneyReflection.TryGetCurrentPowersCategoryOption() ?? 0;
            int offset = point - 10007;
            return option switch
            {
                1 => DescribeJourneyDuplicationPoint(offset),
                2 => offset == 0 ? LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.JourneyMode.Sacrifice.ConfirmFocus",
                    "Research button, press to sacrifice and unlock infinite copies") : null,
                3 => DescribeJourneyTimePoint(offset),
                4 => DescribeJourneyWeatherPoint(offset),
                5 => offset == 0 ? DescribeJourneyPowerFocus("setdifficulty") : null,
                6 => DescribeJourneyPersonalPoint(offset),
                _ => null,
            };
        }

        private static string? GetJourneySpecialSelectionRepeatKey(int point)
        {
            if (!Main.CreativeMenu.Enabled)
            {
                return null;
            }

            if (point == 15000)
            {
                return "journey:sacrifice-slot";
            }

            if (point < 10000 || point > 11000)
            {
                return null;
            }

            if (point <= 10006)
            {
                int offset = point - 10000;
                return offset switch
                {
                    5 => "journey:power:biomespread_setfrozen",
                    6 => "journey:power:setdifficulty",
                    _ => $"journey:main:{offset}",
                };
            }

            int option = JourneyReflection.TryGetCurrentPowersCategoryOption() ?? 0;
            int powerOffset = point - 10007;
            string? powerKey = option switch
            {
                3 => GetJourneyTimePowerKey(powerOffset),
                4 => GetJourneyWeatherPowerKey(powerOffset),
                5 => powerOffset == 0 ? "setdifficulty" : null,
                6 => GetJourneyPersonalPowerKey(powerOffset),
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(powerKey))
            {
                return $"journey:power:{powerKey}";
            }

            return $"journey:option:{option}:{powerOffset}";
        }

        private static string? DescribeJourneyMainStripPoint(int point)
        {
            int offset = point - 10000;
            return offset switch
            {
                0 => FormatButtonLabel(LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.JourneyMode.Powers.Tab.InfiniteItems",
                    "Infinite Items")),
                1 => FormatButtonLabel(LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.JourneyMode.Powers.Tab.Research",
                    "Research")),
                2 => FormatButtonLabel(LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.JourneyMode.Powers.Tab.Time",
                    "Time")),
                3 => FormatButtonLabel(LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.JourneyMode.Powers.Tab.Weather",
                    "Weather")),
                4 => FormatButtonLabel(LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.JourneyMode.Powers.Tab.PersonalPowers",
                    "Personal Powers")),
                5 => DescribeJourneyPowerFocus("biomespread_setfrozen"),
                6 => DescribeJourneyPowerFocus("setdifficulty"),
                _ => null,
            };
        }

        private static string? DescribeJourneyDuplicationPoint(int offset)
        {
            if (offset == 0)
            {
                return LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.JourneyMode.Duplication.SearchFocus",
                    "Search field, type to filter items");
            }

            string? filter = GetJourneyDuplicationFilterName(offset - 1);
            if (string.IsNullOrWhiteSpace(filter))
            {
                return null;
            }

            return string.Format(
                LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.JourneyMode.Duplication.FilterFocusFormat",
                    "Category filter: {0}"),
                filter);
        }

        private static string? DescribeJourneyTimePoint(int offset)
        {
            return offset switch
            {
                0 => DescribeJourneyPowerFocus("time_setfrozen"),
                1 => DescribeJourneyPowerFocus("time_setdawn"),
                2 => DescribeJourneyPowerFocus("time_setnoon"),
                3 => DescribeJourneyPowerFocus("time_setdusk"),
                4 => DescribeJourneyPowerFocus("time_setmidnight"),
                5 or 6 => DescribeJourneyPowerFocus("time_setspeed"),
                _ => null,
            };
        }

        private static bool TryGetJourneyOneShotActivationLabel(int point, out string label)
        {
            label = string.Empty;

            if (!Main.CreativeMenu.Enabled || point < 10007 || point > 11000)
            {
                return false;
            }

            int option = JourneyReflection.TryGetCurrentPowersCategoryOption() ?? 0;
            if (option != 3)
            {
                return false;
            }

            string? key = (point - 10007) switch
            {
                1 => "time_setdawn",
                2 => "time_setnoon",
                3 => "time_setdusk",
                4 => "time_setmidnight",
                _ => null,
            };

            if (key is null || !JourneyPowerRegistry.TryFind(key, out JourneyPowerEntry entry))
            {
                return false;
            }

            string powerLabel = LocalizationHelper.GetTextOrFallback(
                $"Mods.TerrariaAccess.JourneyMode.Power.{entry.LocSuffix}",
                entry.FallbackLabel);
            label = string.Format(
                LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.JourneyMode.Powers.OneShotActivatedFormat",
                    "{0} activated"),
                powerLabel);
            return true;
        }

        private static bool IsJourneyActivationJustPressed()
        {
            return PlayerInput.Triggers.JustPressed.MouseLeft;
        }

        private static string? GetJourneyTimePowerKey(int offset)
        {
            return offset switch
            {
                0 => "time_setfrozen",
                1 => "time_setdawn",
                2 => "time_setnoon",
                3 => "time_setdusk",
                4 => "time_setmidnight",
                5 or 6 => "time_setspeed",
                _ => null,
            };
        }

        private static string? GetJourneyWeatherPowerKey(int offset)
        {
            return offset switch
            {
                0 => "wind_setstrength",
                1 => "wind_setfrozen",
                2 => "rain_setstrength",
                3 => "rain_setfrozen",
                4 => JourneyReflection.TryGetWeatherPowersSubcategoryOption() == 2
                    ? "rain_setstrength"
                    : "wind_setstrength",
                _ => null,
            };
        }

        private static string? GetJourneyPersonalPowerKey(int offset)
        {
            return offset switch
            {
                0 => "godmode",
                1 => "increaseplacementrange",
                2 or 3 => "setspawnrate",
                _ => null,
            };
        }

        private static string? DescribeJourneyWeatherPoint(int offset)
        {
            return offset switch
            {
                0 => DescribeJourneyPowerFocus("wind_setstrength"),
                1 => DescribeJourneyPowerFocus("wind_setfrozen"),
                2 => DescribeJourneyPowerFocus("rain_setstrength"),
                3 => DescribeJourneyPowerFocus("rain_setfrozen"),
                4 => DescribeJourneyPowerFocus(
                    JourneyReflection.TryGetWeatherPowersSubcategoryOption() == 2
                        ? "rain_setstrength"
                        : "wind_setstrength"),
                _ => null,
            };
        }

        private static string? DescribeJourneyPersonalPoint(int offset)
        {
            return offset switch
            {
                0 => DescribeJourneyPowerFocus("godmode"),
                1 => DescribeJourneyPowerFocus("increaseplacementrange"),
                2 or 3 => DescribeJourneyPowerFocus("setspawnrate"),
                _ => null,
            };
        }

        private static string? DescribeJourneyPowerFocus(string key)
        {
            if (!JourneyPowerRegistry.TryFind(key, out JourneyPowerEntry entry))
            {
                return null;
            }

            string label = LocalizationHelper.GetTextOrFallback(
                $"Mods.TerrariaAccess.JourneyMode.Power.{entry.LocSuffix}",
                entry.FallbackLabel);

            object? power = JourneyPowersReflection.TryGetPower(entry.Key);
            Player? player = Main.LocalPlayer;
            int playerIndex = player?.whoAmI ?? Main.myPlayer;

            return entry.Kind switch
            {
                JourneyPowerKind.Toggle or JourneyPowerKind.Shared => FormatJourneyToggleFocus(entry, label, power, playerIndex),
                JourneyPowerKind.Slider => FormatJourneySliderFocus(entry, label, power, playerIndex),
                JourneyPowerKind.OneShot => string.Format(
                    LocalizationHelper.GetTextOrFallback(
                        "Mods.TerrariaAccess.JourneyMode.Powers.FocusOneShotFormat",
                        "{0}, press to activate"),
                    label),
                _ => label,
            };
        }

        private static string FormatJourneyToggleFocus(JourneyPowerEntry entry, string label, object? power, int playerIndex)
        {
            bool? state = power is null ? null : JourneyPowersReflection.TryGetTogglePerPlayerState(power, playerIndex);
            if (!state.HasValue)
            {
                return label;
            }

            string stateWord = state.Value
                ? LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.JourneyMode.State.On", "on")
                : LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.JourneyMode.State.Off", "off");
            return string.Format(
                LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.JourneyMode.Powers.FocusToggleFormat",
                    "{0}, currently {1}"),
                label,
                stateWord);
        }

        private static string FormatJourneySliderFocus(JourneyPowerEntry entry, string label, object? power, int playerIndex)
        {
            float? value = power is null ? null : JourneyPowersReflection.TryGetSliderValue(power, playerIndex);
            if (!value.HasValue)
            {
                return label;
            }

            string valueText = JourneySliderValueFormatter.Format(entry.Key, value.Value);
            return string.Format(
                LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.JourneyMode.Powers.FocusSliderFormat",
                    "{0}, {1}"),
                label,
                valueText);
        }

        private static string? DescribeJourneySacrificeSlot()
        {
            Item? sacrifice = JourneyReflection.TryGetSacrificeItem();
            if (sacrifice is null || sacrifice.IsAir)
            {
                return LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.JourneyMode.Sacrifice.FocusEmpty",
                    "Sacrifice slot, empty");
            }

            Player? player = Main.LocalPlayer;
            var tracker = player?.creativeTracker?.ItemSacrifices;
            string itemName = sacrifice.AffixName();
            int itemId = sacrifice.type;

            bool canResearch = CreativeItemSacrificesCatalog.Instance
                .TryGetSacrificeCountCapToUnlockInfiniteItems(itemId, out _);
            int have = 0;
            int needed = 0;
            bool hasNumbers = tracker?.TryGetSacrificeNumbers(itemId, out have, out needed) == true;
            bool fully = canResearch && hasNumbers && needed > 0 && have >= needed;

            if (fully)
            {
                return string.Format(
                    LocalizationHelper.GetTextOrFallback(
                        "Mods.TerrariaAccess.JourneyMode.Sacrifice.FullyResearched",
                        "Sacrifice slot: {0}, fully researched"),
                    itemName);
            }

            if (canResearch && hasNumbers)
            {
                return string.Format(
                    LocalizationHelper.GetTextOrFallback(
                        "Mods.TerrariaAccess.JourneyMode.Sacrifice.ProgressFormat",
                        "Sacrifice slot: {0}, {1} of {2} to research"),
                    itemName,
                    have,
                    needed);
            }

            return string.Format(
                LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.JourneyMode.Sacrifice.CannotResearch",
                    "Sacrifice slot: {0}, cannot be researched"),
                itemName);
        }

        private static string? GetJourneyDuplicationFilterName(int index)
        {
            string key = index switch
            {
                0 => "CreativePowers.TabWeapons",
                1 => "CreativePowers.TabArmor",
                2 => "CreativePowers.TabVanity",
                3 => "CreativePowers.TabBlocks",
                4 => "CreativePowers.TabFurniture",
                5 => "CreativePowers.TabAccessories",
                6 => "CreativePowers.TabAccessoriesMisc",
                7 => "CreativePowers.TabConsumables",
                8 => "CreativePowers.TabTools",
                9 => "CreativePowers.TabMaterials",
                10 => "CreativePowers.TabMisc",
                _ => string.Empty,
            };

            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            string fallback = index switch
            {
                0 => "Weapons",
                1 => "Armor",
                2 => "Vanity",
                3 => "Blocks",
                4 => "Furniture",
                5 => "Accessories",
                6 => "Miscellaneous accessories",
                7 => "Consumables",
                8 => "Tools",
                9 => "Materials",
                10 => "Miscellaneous",
                _ => string.Empty,
            };

            return LocalizationHelper.GetTextOrFallback(key, fallback);
        }

        private static string? FormatButtonLabel(string? text)
        {
            string cleaned = TextSanitizer.Clean(text ?? string.Empty);
            return string.IsNullOrWhiteSpace(cleaned) ? null : $"{cleaned} button";
        }

        private static string? GetPvpToggleText()
        {
            Player? player = Main.LocalPlayer;
            bool hostile = player?.hostile ?? false;
            string key = hostile
                ? "Mods.TerrariaAccess.InventorySpecial.DisablePvp"
                : "Mods.TerrariaAccess.InventorySpecial.EnablePvp";
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
                0 => "Mods.TerrariaAccess.InventorySpecial.TeamNeutral",
                1 => "Mods.TerrariaAccess.InventorySpecial.TeamRed",
                2 => "Mods.TerrariaAccess.InventorySpecial.TeamGreen",
                3 => "Mods.TerrariaAccess.InventorySpecial.TeamBlue",
                4 => "Mods.TerrariaAccess.InventorySpecial.TeamYellow",
                5 => "Mods.TerrariaAccess.InventorySpecial.TeamPink",
                _ => string.Empty,
            };

            return LocalizationHelper.GetTextOrFallback(key, fallbacks[teamIndex]);
        }

        private static string? DescribeDefenseCounter()
        {
            Player? player = Main.LocalPlayer;
            int defense = player?.statDefense ?? 0;
            string label = LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.InventorySpecial.Defense", "Defense");
            string cleaned = TextSanitizer.Clean(label);
            return $"{cleaned} {defense}";
        }

        private static string? DescribeBuilderAccessoryToggle(int point)
        {
            Player? player = Main.LocalPlayer;
            if (player is null || player.builderAccStatus is null)
            {
                return null;
            }

            int offset = point - 6000;
            int visibleCount = UILinkPointNavigator.Shortcuts.BUILDERACCCOUNT;
            if (offset < 0 || visibleCount <= 0 || offset >= visibleCount)
            {
                return null;
            }

            int index = 0;
            if (offset == index++)
            {
                return FormatButtonLabel(GetToggleText(
                    player.builderAccStatus[Player.BuilderAccToggleIDs.BlockSwap] == 0,
                    "GameUI.BlockReplacerOn",
                    "GameUI.BlockReplacerOff"));
            }

            if (player.unlockedBiomeTorches)
            {
                if (offset == index++)
                {
                    return FormatButtonLabel(GetToggleText(
                        player.builderAccStatus[Player.BuilderAccToggleIDs.TorchBiome] == 0,
                        "GameUI.TorchTypeSwapperOn",
                        "GameUI.TorchTypeSwapperOff"));
                }
            }

            int[] drawOrder =
            {
                Player.BuilderAccToggleIDs.HideAllWires,
                Player.BuilderAccToggleIDs.WireVisibility_Actuators,
                Player.BuilderAccToggleIDs.RulerLine,
                Player.BuilderAccToggleIDs.RulerGrid,
                Player.BuilderAccToggleIDs.AutoActuate,
                Player.BuilderAccToggleIDs.AutoPaint,
                Player.BuilderAccToggleIDs.WireVisibility_Red,
                Player.BuilderAccToggleIDs.WireVisibility_Green,
                Player.BuilderAccToggleIDs.WireVisibility_Blue,
                Player.BuilderAccToggleIDs.WireVisibility_Yellow,
            };

            foreach (int toggleId in drawOrder)
            {
                if (!IsBuilderToggleVisible(player, toggleId))
                {
                    continue;
                }

                if (offset == index++)
                {
                    return FormatButtonLabel(DescribeVisibleBuilderToggle(player, toggleId));
                }
            }

            return null;
        }

        private static bool IsBuilderToggleVisible(Player player, int toggleId)
        {
            return toggleId switch
            {
                Player.BuilderAccToggleIDs.HideAllWires or
                Player.BuilderAccToggleIDs.WireVisibility_Red or
                Player.BuilderAccToggleIDs.WireVisibility_Green or
                Player.BuilderAccToggleIDs.WireVisibility_Blue or
                Player.BuilderAccToggleIDs.WireVisibility_Yellow or
                Player.BuilderAccToggleIDs.WireVisibility_Actuators => player.InfoAccMechShowWires,
                Player.BuilderAccToggleIDs.RulerLine => player.rulerLine,
                Player.BuilderAccToggleIDs.RulerGrid => player.rulerGrid,
                Player.BuilderAccToggleIDs.AutoActuate => player.autoActuator,
                Player.BuilderAccToggleIDs.AutoPaint => player.autoPaint,
                _ => false,
            };
        }

        private static string? DescribeVisibleBuilderToggle(Player player, int toggleId)
        {
            int status = player.builderAccStatus[toggleId];
            return toggleId switch
            {
                Player.BuilderAccToggleIDs.HideAllWires => GetToggleText(
                    status == 0,
                    "GameUI.WireModeForced",
                    "GameUI.WireModeNormal"),
                Player.BuilderAccToggleIDs.RulerLine => GetToggleText(
                    status == 0,
                    "GameUI.RulerOn",
                    "GameUI.RulerOff"),
                Player.BuilderAccToggleIDs.RulerGrid => GetToggleText(
                    status == 0,
                    "GameUI.MechanicalRulerOn",
                    "GameUI.MechanicalRulerOff"),
                Player.BuilderAccToggleIDs.AutoActuate => GetToggleText(
                    status == 0,
                    "GameUI.ActuationDeviceOn",
                    "GameUI.ActuationDeviceOff"),
                Player.BuilderAccToggleIDs.AutoPaint => GetToggleText(
                    status == 0,
                    "GameUI.PaintSprayerOn",
                    "GameUI.PaintSprayerOff"),
                Player.BuilderAccToggleIDs.WireVisibility_Red or
                Player.BuilderAccToggleIDs.WireVisibility_Green or
                Player.BuilderAccToggleIDs.WireVisibility_Blue or
                Player.BuilderAccToggleIDs.WireVisibility_Yellow or
                Player.BuilderAccToggleIDs.WireVisibility_Actuators => DescribeWireVisibilityToggle(status, toggleId),
                _ => null,
            };
        }

        private static string GetToggleText(bool enabled, string enabledKey, string disabledKey)
        {
            return Language.GetTextValue(enabled ? enabledKey : disabledKey);
        }

        private static string DescribeWireVisibilityToggle(int status, int toggleId)
        {
            string target = toggleId switch
            {
                Player.BuilderAccToggleIDs.WireVisibility_Red => Language.GetTextValue("Game.RedWires"),
                Player.BuilderAccToggleIDs.WireVisibility_Green => Language.GetTextValue("Game.GreenWires"),
                Player.BuilderAccToggleIDs.WireVisibility_Blue => Language.GetTextValue("Game.BlueWires"),
                Player.BuilderAccToggleIDs.WireVisibility_Yellow => Language.GetTextValue("Game.YellowWires"),
                Player.BuilderAccToggleIDs.WireVisibility_Actuators => Language.GetTextValue("Game.Actuators"),
                _ => string.Empty,
            };
            string mode = status switch
            {
                0 => Language.GetTextValue("GameUI.Bright"),
                1 => Language.GetTextValue("GameUI.Normal"),
                2 => Language.GetTextValue("GameUI.Faded"),
                3 => Language.GetTextValue("GameUI.Hidden"),
                _ => Language.GetTextValue("GameUI.Normal"),
            };

            return string.IsNullOrWhiteSpace(target) ? mode : $"{target}: {mode}";
        }

        private sealed class SpecialSelectionRepeatGuard
        {
            private string? _lastKey;

            public bool ShouldAnnounce(string key)
            {
                return !string.IsNullOrWhiteSpace(key) &&
                    !string.Equals(key, _lastKey, StringComparison.Ordinal);
            }

            public void Record(string key)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    Clear();
                    return;
                }

                _lastKey = key;
            }

            public void Clear()
            {
                _lastKey = null;
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
            TerrariaAccess.Instance?.Logger.Debug($"[IngameOptionsNarration] point={currentPoint} feature={feature} cat={category} left={leftHover} rightHover={rightHover} rightLock={rightLock} hoverIsAir={hoverIsAir} location='{location}'");
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
                TerrariaAccess.Instance?.Logger.Debug($"[IngameOptionsNarration] Unable to read {field.Name}: {ex.Message}");
            }

            return -1;
        }

        private static bool ShouldLogUnknownInventoryPoint(int point, bool hoverIsAir, string? location)
        {
            if (!PlayerInput.UsingGamepadUI)
            {
                return false;
            }

            if (!hoverIsAir)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(location))
            {
                return false;
            }

            if (SlotNavigationHelper.IsInventoryLinkPoint(point) ||
                SlotNavigationHelper.IsChestLinkPoint(point) ||
                SlotNavigationHelper.IsCraftingGridLinkPoint(point) ||
                SlotNavigationHelper.IsCraftingListLinkPoint(point))
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
            TerrariaAccess.Instance?.Logger.Info(
                $"[InventoryNarration] Unknown UI link point {point} (hoverIsAir={hoverIsAir}, location='{location ?? string.Empty}', usingGamepad={usingGamepad}, inventory={inventoryOpen}, state={state})");
        }
    }
}
