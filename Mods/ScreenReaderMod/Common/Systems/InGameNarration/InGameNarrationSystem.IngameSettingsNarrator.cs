#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.MenuNarration;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.BigProgressBar;
using Terraria.GameContent.Events;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;
using Terraria.UI.Gamepad;
using Terraria.UI.Chat;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class IngameSettingsNarrator
    {
        private static readonly string[] DefaultCategoryLabels =
        {
            TextSanitizer.Clean(Lang.menu[114].Value),
            TextSanitizer.Clean(Lang.menu[210].Value),
            TextSanitizer.Clean(Lang.menu[63].Value),
            TextSanitizer.Clean(Lang.menu[65].Value),
            TextSanitizer.Clean(Lang.menu[218].Value),
            TextSanitizer.Clean(Lang.menu[219].Value),
            TextSanitizer.Clean(Lang.menu[103].Value),
        };

        private static readonly string[] CategoryLabelOverrides = BuildCategoryLabelOverrides();

        private static readonly Dictionary<int, string> CategoryFallbackLabels = new()
        {
            [0] = TextSanitizer.Clean(Lang.menu[114].Value),
            [1] = TextSanitizer.Clean(Lang.menu[210].Value),
            [2] = TextSanitizer.Clean(Lang.menu[63].Value),
            [3] = TextSanitizer.Clean(Lang.menu[65].Value),
            [4] = TextSanitizer.Clean(Lang.menu[218].Value),
            [5] = TextSanitizer.Clean(Lang.menu[219].Value),
        };

        private static readonly Dictionary<string, int> CategoryLabelLookup = BuildCategoryLookup();

        private FieldInfo? _leftHoverField;
        private FieldInfo? _leftLockField;
        private FieldInfo? _rightHoverField;
        private FieldInfo? _rightLockField;
        private FieldInfo? _categoryField;
        private FieldInfo? _mouseOverTextField;
        private bool _fieldsResolved;
        private FieldInfo? _leftSideCategoryMappingField;
        private FieldInfo? _skipRightSlotField;
        private static string[] BuildCategoryLabelOverrides()
        {
            return new[]
            {
                TextSanitizer.Clean(Lang.menu[114].Value),
                TextSanitizer.Clean(Lang.menu[218].Value),
                TextSanitizer.Clean(Lang.menu[219].Value),
                ResolveModConfigurationLabel(),
                TextSanitizer.Clean(Lang.menu[131].Value),
                LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.IngameOptions.CloseMenu", "Close Menu"),
                LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.IngameOptions.SaveAndExit", "Save & Exit"),
            };
        }

        private static string ResolveModConfigurationLabel()
        {
            string[] candidates =
            {
                "tModLoader.ModConfiguration",
                "tModLoader.MenuModConfiguration",
                "ModConfiguration",
            };

            foreach (string key in candidates)
            {
                string value = TryGetLanguageValue(key);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.IngameOptions.ModConfiguration", "Mod Configuration");
        }

        private static string TryGetLanguageValue(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            try
            {
                LocalizedText text = Language.GetText(key);
                string value = text?.Value ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value, key, StringComparison.Ordinal))
                {
                    return TextSanitizer.Clean(value);
                }
            }
            catch (Exception ex)
            {
                ScreenReaderMod.Instance?.Logger.Debug($"[IngameSettings] Language key '{key}' lookup failed: {ex.Message}");
            }

            return string.Empty;
        }

        private static bool _loggedFieldCatalog;

        private int _lastLeftHover = int.MinValue;
        private int _lastLoggedLeftHover = int.MinValue;
        private int _lastLoggedRightHover = int.MinValue;
        private int _lastLoggedCategory = int.MinValue;
        private int _lastCategory = int.MinValue;
        private int _lastRightHover = int.MinValue;
        private int _lastRightLock = int.MinValue;
        private int _lastSpecialFeature = int.MinValue;
        private string? _lastCategoryLabel;
        private float _lastMusicVolume = -1f;
        private float _lastSoundVolume = -1f;
        private float _lastAmbientVolume = -1f;
        private int _lastParallax = int.MinValue;

        public void PrimeReflection()
        {
            EnsureReflection();
        }

        public void Update()
        {
            if (!Main.ingameOptionsWindow)
            {
                Reset();
                return;
            }

            EnsureReflection();

            int leftHover = ReadInt(_leftHoverField);
            int leftLock = ReadInt(_leftLockField);
            int rightHover = ReadInt(_rightHoverField);
            int rightLock = ReadInt(_rightLockField);
            int rawCategory = ReadInt(_categoryField);
            int special = UILinkPointNavigator.Shortcuts.OPTIONS_BUTTON_SPECIALFEATURE;

            LogStateChanges(leftHover, rightHover, rawCategory, special);

            if (leftHover >= 0 && leftHover != _lastLeftHover)
            {
                string? label = GetLeftCategoryLabel(leftHover, allowMouseTextFallback: true);
                if (!string.IsNullOrWhiteSpace(label))
                {
                    ScreenReaderService.Announce(label);
                }

                _lastLeftHover = leftHover;
            }

            int selectedLeftIndex = leftLock >= 0 ? leftLock : leftHover;
            int categoryId = ResolveCategoryId(rawCategory, selectedLeftIndex);

            string? categoryLabel = GetCategoryLabelById(categoryId, selectedLeftIndex, leftHover);
            if (!string.IsNullOrWhiteSpace(categoryLabel))
            {
                bool categoryChanged = categoryId != _lastCategory;
                if (!categoryChanged && _lastCategoryLabel is not null)
                {
                    categoryChanged = !string.Equals(categoryLabel, _lastCategoryLabel, StringComparison.Ordinal);
                }

                if (categoryChanged)
                {
                    ScreenReaderService.Announce(categoryLabel);
                    _lastCategory = categoryId;
                    _lastCategoryLabel = categoryLabel;
                }
            }

            if (rightHover < 0)
            {
                rightHover = rightLock;
            }

            if (categoryId >= 0 &&
                rightHover >= 0 &&
                !IngameOptionsLabelTracker.IsOptionSkipped(rightHover) &&
                (rightHover != _lastRightHover || categoryId != _lastRightLock))
            {
                string? description = DescribeOption(categoryId, rightHover, categoryLabel);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    ScreenReaderService.Announce(description);
                }

                _lastRightHover = rightHover;
                _lastRightLock = categoryId;
            }

            AnnounceSpecialFeature(special);
            _lastSpecialFeature = special;
        }

        private void AnnounceSpecialFeature(int specialFeature)
        {
            switch (specialFeature)
            {
                case 1:
                {
                    int parallax = Utils.Clamp(Main.bgScroll, 0, 100);
                    if (parallax != _lastParallax)
                    {
                        ScreenReaderService.Announce($"Background parallax {parallax} percent");
                        _lastParallax = parallax;
                    }
                    break;
                }
                case 2:
                {
                    float value = MathF.Round(Main.musicVolume * 100f);
                    if (Math.Abs(value - _lastMusicVolume) >= 1f || _lastSpecialFeature != specialFeature)
                    {
                        ScreenReaderService.Announce($"Music volume {value:0}%");
                        _lastMusicVolume = value;
                    }
                    break;
                }
                case 3:
                {
                    float value = MathF.Round(Main.soundVolume * 100f);
                    if (Math.Abs(value - _lastSoundVolume) >= 1f || _lastSpecialFeature != specialFeature)
                    {
                        ScreenReaderService.Announce($"Sound volume {value:0}%");
                        _lastSoundVolume = value;
                    }
                    break;
                }
                case 4:
                {
                    float value = MathF.Round(Main.ambientVolume * 100f);
                    if (Math.Abs(value - _lastAmbientVolume) >= 1f || _lastSpecialFeature != specialFeature)
                    {
                        ScreenReaderService.Announce($"Ambient volume {value:0}%");
                        _lastAmbientVolume = value;
                    }
                    break;
                }
            }
        }

        private void LogStateChanges(int leftHover, int rightHover, int category, int special)
        {
            if (ScreenReaderMod.Instance is null)
            {
                return;
            }

            if (leftHover == _lastLoggedLeftHover &&
                rightHover == _lastLoggedRightHover &&
                category == _lastLoggedCategory &&
                special == _lastSpecialFeature)
            {
                return;
            }

            _lastLoggedLeftHover = leftHover;
            _lastLoggedRightHover = rightHover;
            _lastLoggedCategory = category;

            string hoverText = ReadString(_mouseOverTextField);

            ScreenReaderMod.Instance.Logger.Debug(
                $"[IngameSettings] leftHover={leftHover}, rightHover={rightHover}, category={category}, specialFeature={special}, mouseOverText=\"{hoverText}\"");
        }

        private void EnsureReflection()
        {
            if (_fieldsResolved)
            {
                return;
            }

            try
            {
                Type optionsType = typeof(IngameOptions);
                BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

                if (!_loggedFieldCatalog)
                {
                    foreach (FieldInfo field in optionsType.GetFields(flags).OrderBy(f => f.Name))
                    {
                        ScreenReaderMod.Instance?.Logger.Debug($"[IngameSettings] field: {field.FieldType.FullName} {field.Name}");
                    }

                    foreach (PropertyInfo property in optionsType.GetProperties(flags).OrderBy(p => p.Name))
                    {
                        ScreenReaderMod.Instance?.Logger.Debug($"[IngameSettings] property: {property.PropertyType.FullName} {property.Name}");
                    }

                    _loggedFieldCatalog = true;
                }

                FieldInfo[] fields = optionsType.GetFields(flags);
                _leftHoverField = FindIntField(fields, "left", "hover");
                _leftLockField = FindIntField(fields, "left", "lock");
                _rightHoverField = FindIntField(fields, "right", "hover");
                _rightLockField = FindIntField(fields, "right", "lock");
                _categoryField = FindIntField(fields, "category");
                _mouseOverTextField = FindStringField(fields, "mouse", "over", "text");
                _leftSideCategoryMappingField ??= optionsType.GetField("_leftSideCategoryMapping", flags);
                _skipRightSlotField ??= optionsType.GetField("skipRightSlot", flags);

                IngameOptionsLabelTracker.Configure(_leftSideCategoryMappingField, _skipRightSlotField, _categoryField);

                _fieldsResolved = true;

                ScreenReaderMod.Instance?.Logger.Debug("[IngameSettings] Reflection resolved: " +
                    $"leftHover={_leftHoverField?.Name ?? "null"}, leftLock={_leftLockField?.Name ?? "null"}, " +
                    $"rightHover={_rightHoverField?.Name ?? "null"}, rightLock={_rightLockField?.Name ?? "null"}, " +
                    $"category={_categoryField?.Name ?? "null"}, mapping={_leftSideCategoryMappingField?.Name ?? "null"}, skipRightSlot={_skipRightSlotField?.Name ?? "null"}");
            }
            catch (Exception ex)
            {
                ScreenReaderMod.Instance?.Logger.Warn($"[IngameSettings] Reflection resolution failed: {ex.Message}");
                _fieldsResolved = true;
            }
        }

        private static FieldInfo? FindIntField(IEnumerable<FieldInfo> fields, params string[] keywords)
        {
            return fields.FirstOrDefault(field =>
                field.FieldType == typeof(int) &&
                keywords.All(k => field.Name.Contains(k, StringComparison.OrdinalIgnoreCase)));
        }

        private static FieldInfo? FindStringField(IEnumerable<FieldInfo> fields, params string[] keywords)
        {
            return fields.FirstOrDefault(field =>
                field.FieldType == typeof(string) &&
                keywords.All(k => field.Name.Contains(k, StringComparison.OrdinalIgnoreCase)));
        }

        private static int ReadInt(FieldInfo? field)
        {
            try
            {
                if (field is not null && field.GetValue(null) is int value)
                {
                    return value;
                }
            }
            catch (Exception ex)
            {
                ScreenReaderMod.Instance?.Logger.Debug($"[IngameSettings] Failed to read {field?.Name}: {ex.Message}");
            }

            return -1;
        }

        private static string ReadString(FieldInfo? field)
        {
            try
            {
                if (field is not null && field.GetValue(null) is string value)
                {
                    return value;
                }
            }
            catch (Exception ex)
            {
                ScreenReaderMod.Instance?.Logger.Debug($"[IngameSettings] Failed to read {field?.Name}: {ex.Message}");
            }

            return string.Empty;
        }

        private static string ConvertOptionEntry(object? entry)
        {
            if (entry is null)
            {
                return string.Empty;
            }

            switch (entry)
            {
                case string str:
                    return TextSanitizer.Clean(str);
                case LocalizedText localized:
                    return TextSanitizer.Clean(localized.Value);
                case float or double:
                    return string.Empty;
                case int menuIndex:
                    return LookupMenu(menuIndex);
                case sbyte signedByte:
                    return ConvertOptionEntry((int)signedByte);
                case byte byteValue:
                    return ConvertOptionEntry((int)byteValue);
                case short shortValue:
                    return ConvertOptionEntry((int)shortValue);
                case ushort ushortValue:
                    return ConvertOptionEntry((int)ushortValue);
                case uint uintValue when uintValue <= int.MaxValue:
                    return ConvertOptionEntry((int)uintValue);
                case uint uintValue:
                    return uintValue.ToString();
                case long longValue when longValue >= int.MinValue && longValue <= int.MaxValue:
                    return ConvertOptionEntry((int)longValue);
                case long longValue:
                    return longValue.ToString();
                case Enum enumValue:
                    return ConvertOptionEntry(Convert.ToInt32(enumValue));
                case Delegate del when del.Method.GetParameters().Length == 0:
                    try
                    {
                        object? result = del.DynamicInvoke();
                        string converted = ConvertOptionEntry(result);
                        if (!string.IsNullOrWhiteSpace(converted))
                        {
                            return converted;
                        }
                    }
                    catch (Exception ex)
                    {
                        ScreenReaderMod.Instance?.Logger.Debug($"[IngameSettings] Delegate conversion failed: {ex.Message}");
                    }
                    break;
            }

            string[] preferredMembers =
            {
                "Label",
                "Text",
                "DisplayName",
                "Caption",
                "Name",
                "Description",
                "Tooltip",
            };

            Type type = entry.GetType();
            foreach (string member in preferredMembers)
            {
                string value = TryReadMemberText(type, entry, member);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return TextSanitizer.Clean(value);
                }
            }

            MethodInfo? getDisplayText = type.GetMethod("GetDisplayText", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (getDisplayText is not null)
            {
                try
                {
                    object? value = getDisplayText.Invoke(entry, Array.Empty<object>());
                    string converted = ConvertOptionEntry(value);
                    if (!string.IsNullOrWhiteSpace(converted))
                    {
                        return converted;
                    }
                }
                catch (Exception ex)
                {
                    ScreenReaderMod.Instance?.Logger.Debug($"[IngameSettings] GetDisplayText invocation failed: {ex.Message}");
                }
            }

            return TextSanitizer.Clean(entry.ToString() ?? string.Empty);
        }

        private static string LookupMenu(int index)
        {
            if (index < 0)
            {
                return string.Empty;
            }

            try
            {
                LocalizedText[] menu = Lang.menu;
                if (index < menu.Length)
                {
                    string value = menu[index].Value;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return TextSanitizer.Clean(value);
                    }
                }
            }
            catch (Exception ex)
            {
                ScreenReaderMod.Instance?.Logger.Debug($"[IngameSettings] LookupMenu failed for {index}: {ex.Message}");
            }

            return string.Empty;
        }

        private static string TryReadMemberText(Type type, object instance, string memberName)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            PropertyInfo? property = type.GetProperty(memberName, flags);
            if (property is not null && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    object? value = property.GetValue(instance, null);
                    if (value is not null && !ReferenceEquals(instance, value))
                    {
                        string text = ConvertOptionEntry(value);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text;
                        }
                    }
                }
                catch
                {
                    // ignore property getter failures
                }
            }

            FieldInfo? field = type.GetField(memberName, flags);
            if (field is not null)
            {
                try
                {
                    object? value = field.GetValue(instance);
                    if (value is not null && !ReferenceEquals(instance, value))
                    {
                        string text = ConvertOptionEntry(value);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text;
                        }
                    }
                }
                catch
                {
                    // ignore field access failures
                }
            }

            return string.Empty;
        }

        private string? GetLeftCategoryLabel(int leftIndex, bool allowMouseTextFallback)
        {
            if (leftIndex < 0)
            {
                return null;
            }

            if (IngameOptionsLabelTracker.TryGetLeftLabel(leftIndex, out string label) && !string.IsNullOrWhiteSpace(label))
            {
                return label;
            }

            if (allowMouseTextFallback)
            {
                string mouseText = ReadString(_mouseOverTextField);
                if (!string.IsNullOrWhiteSpace(mouseText))
                {
                    return TextSanitizer.Clean(mouseText);
                }
            }

            if ((uint)leftIndex < (uint)CategoryLabelOverrides.Length)
            {
                string overrideLabel = CategoryLabelOverrides[leftIndex];
                if (!string.IsNullOrWhiteSpace(overrideLabel))
                {
                    return overrideLabel;
                }
            }

            if ((uint)leftIndex < (uint)DefaultCategoryLabels.Length)
            {
                return DefaultCategoryLabels[leftIndex];
            }

            return null;
        }

        private string? GetCategoryLabelById(int categoryId, int selectedLeftIndex, int leftHover)
        {
            if (categoryId >= 0 && IngameOptionsLabelTracker.TryGetCategoryLabel(categoryId, out string label) && !string.IsNullOrWhiteSpace(label))
            {
                return label;
            }

            if (selectedLeftIndex >= 0 && IngameOptionsLabelTracker.TryGetLeftLabel(selectedLeftIndex, out string leftLabel) && !string.IsNullOrWhiteSpace(leftLabel))
            {
                return leftLabel;
            }

            if (categoryId >= 0 &&
                CategoryFallbackLabels.TryGetValue(categoryId, out string? fallbackLabel) &&
                !string.IsNullOrWhiteSpace(fallbackLabel))
            {
                return fallbackLabel;
            }

            if (selectedLeftIndex >= 0)
            {
                bool allowMouseFallback = leftHover == selectedLeftIndex;
                return GetLeftCategoryLabel(selectedLeftIndex, allowMouseTextFallback: allowMouseFallback);
            }

            return null;
        }

        private int ResolveCategoryId(int rawCategory, int selectedLeftIndex)
        {
            if (rawCategory >= 0)
            {
                return rawCategory;
            }

            if (selectedLeftIndex >= 0 && IngameOptionsLabelTracker.TryMapLeftToCategory(selectedLeftIndex, out int mapped))
            {
                return mapped;
            }

            return rawCategory;
        }

        private string? DescribeOption(int category, int option, string? categoryLabel)
        {
            if (category < 0 || option < 0)
            {
                return null;
            }

            if (IngameOptionsLabelTracker.TryGetOptionLabel(category, option, out string label) && !string.IsNullOrWhiteSpace(label))
            {
                return label;
            }

            string mouseText = ReadString(_mouseOverTextField);
            if (!string.IsNullOrWhiteSpace(mouseText))
            {
                return TextSanitizer.Clean(mouseText);
            }

            return DescribeFallback(category, option, categoryLabel);
        }

        private static string? DescribeFallback(int category, int option, string? categoryLabel)
        {
            int normalizedCategory = NormalizeCategoryId(category, categoryLabel);

            try
            {
                return normalizedCategory switch
                {
                    0 => DescribeGeneral(option),
                    1 => DescribeInterface(option),
                    2 => DescribeVideo(option),
                    3 => DescribeAudio(option),
                    4 => DescribeCursor(option),
                    5 => DescribeGameplay(option),
                    _ => null,
                };
            }
            catch (Exception ex)
            {
                ScreenReaderMod.Instance?.Logger.Debug($"[IngameSettings] Fallback description failed: {ex.Message}");
                return null;
            }
        }

        private static string DescribeGeneral(int option)
        {
            string result = option switch
            {
                0 => Main.autoSave ? Lang.menu[67].Value : Lang.menu[68].Value,
                1 => Main.autoPause ? Lang.menu[69].Value : Lang.menu[70].Value,
                2 => Main.mapEnabled ? Lang.menu[112].Value : Lang.menu[113].Value,
                3 => Main.HidePassword ? Lang.menu[212].Value : Lang.menu[211].Value,
                4 => Lang.menu[5].Value,
                _ => $"General option {option + 1}",
            };

            return TextSanitizer.Clean(result);
        }

        private static string DescribeAudio(int option)
        {
            string result = option switch
            {
                0 => $"{Lang.menu[98].Value}: {MathF.Round(Main.musicVolume * 100f):0}%",
                1 => $"{Lang.menu[99].Value}: {MathF.Round(Main.soundVolume * 100f):0}%",
                2 => $"{Lang.menu[119].Value}: {MathF.Round(Main.ambientVolume * 100f):0}%",
                3 => Lang.menu[5].Value,
                _ => $"Audio option {option + 1}",
            };

            return TextSanitizer.Clean(result);
        }

        private static string DescribeInterface(int option)
        {
            string mapBorder = string.Empty;
            try
            {
                string key = Main.MinimapFrameManagerInstance?.ActiveSelectionKeyName ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(key))
                {
                    mapBorder = Language.GetTextValue("UI.MinimapFrame_" + key);
                }
            }
            catch
            {
                mapBorder = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(mapBorder))
            {
                mapBorder = Language.GetTextValue("UI.MinimapFrame_Classic");
            }

            string result = option switch
            {
                0 => Main.showItemText ? Lang.menu[71].Value : Lang.menu[72].Value,
                1 => $"{Lang.menu[123].Value} {Lang.menu[124 + Utils.Clamp(Main.invasionProgressMode, 0, 2)].Value}",
                2 => Main.placementPreview ? Lang.menu[128].Value : Lang.menu[129].Value,
                3 => ItemSlot.Options.HighlightNewItems ? Lang.inter[117].Value : Lang.inter[116].Value,
                4 => Main.MouseShowBuildingGrid ? Lang.menu[229].Value : Lang.menu[230].Value,
                5 => Main.GamepadDisableInstructionsDisplay ? Lang.menu[241].Value : Lang.menu[242].Value,
                6 => Language.GetTextValue("UI.SelectMapBorder", mapBorder),
                7 => Language.GetTextValue("UI.SelectHealthStyle", Main.ResourceSetsManager?.ActiveSet.DisplayedName ?? string.Empty),
                8 => Language.GetTextValue(BigProgressBarSystem.ShowText ? "UI.ShowBossLifeTextOn" : "UI.ShowBossLifeTextOff"),
                9 => Language.GetTextValue("tModLoader.BossBarStyle", Terraria.ModLoader.BossBarLoader.CurrentStyle?.DisplayName ?? string.Empty),
                10 => Lang.menu[5].Value,
                _ => $"Interface option {option + 1}",
            };

            return TextSanitizer.Clean(result);
        }

        private static string DescribeVideo(int option)
        {
            int frameSkipIndex = (int)Main.FrameSkipMode;
            string result = option switch
            {
                0 => Lang.menu[51].Value,
                1 => Lang.menu[52].Value,
                2 => Lang.menu[247 + Utils.Clamp(frameSkipIndex, 0, 3)].Value,
                3 => Language.GetTextValue("UI.LightMode_" + Lighting.Mode),
                4 => Main.qaStyle switch
                {
                    0 => Lang.menu[59].Value,
                    1 => Lang.menu[60].Value,
                    2 => Lang.menu[61].Value,
                    _ => Lang.menu[62].Value,
                },
                5 => Main.BackgroundEnabled ? Lang.menu[100].Value : Lang.menu[101].Value,
                6 => ChildSafety.Disabled ? Lang.menu[132].Value : Lang.menu[133].Value,
                7 => Main.SettingsEnabled_MinersWobble ? Lang.menu[250].Value : Lang.menu[251].Value,
                8 => Main.SettingsEnabled_TilesSwayInWind ? Language.GetTextValue("UI.TilesSwayInWindOn") : Language.GetTextValue("UI.TilesSwayInWindOff"),
                9 => Language.GetTextValue("UI.Effects"),
                10 => Lang.menu[5].Value,
                _ => $"Video option {option + 1}",
            };

            return TextSanitizer.Clean(result);
        }

        private static string DescribeCursor(int option)
        {
            string result = option switch
            {
                0 => Lang.menu[214].Value,
                1 => Lang.menu[215].Value,
                2 => Lang.menu[216].Value,
                3 => Lang.menu[217].Value,
                4 => Lang.menu[218].Value,
                5 => Lang.menu[219].Value,
                6 => Lang.menu[5].Value,
                _ => $"Cursor option {option + 1}",
            };

            return TextSanitizer.Clean(result);
        }

        private static string DescribeGameplay(int option)
        {
            string result = option switch
            {
                0 => Lang.menu[220].Value,
                1 => Lang.menu[221].Value,
                2 => Lang.menu[222].Value,
                3 => Lang.menu[5].Value,
                _ => $"Gameplay option {option + 1}",
            };

            return TextSanitizer.Clean(result);
        }

        private static int NormalizeCategoryId(int category, string? categoryLabel)
        {
            if (!string.IsNullOrWhiteSpace(categoryLabel) && CategoryLabelLookup.TryGetValue(categoryLabel, out int mapped))
            {
                return mapped;
            }

            if (category >= 0)
            {
                return category;
            }

            return -1;
        }

        private static Dictionary<string, int> BuildCategoryLookup()
        {
            var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            void AddMapping(int id, string? label)
            {
                string sanitized = TextSanitizer.Clean(label ?? string.Empty);
                if (string.IsNullOrWhiteSpace(sanitized))
                {
                    return;
                }

                lookup[sanitized] = id;
            }

            foreach ((int id, string label) in CategoryFallbackLabels)
            {
                AddMapping(id, label);
            }

            for (int i = 0; i < DefaultCategoryLabels.Length; i++)
            {
                AddMapping(i, DefaultCategoryLabels[i]);
            }

            for (int i = 0; i < CategoryLabelOverrides.Length; i++)
            {
                AddMapping(i, CategoryLabelOverrides[i]);
            }

            return lookup;
        }

        private void Reset()
        {
            _lastLeftHover = int.MinValue;
            _lastLoggedLeftHover = int.MinValue;
            _lastLoggedRightHover = int.MinValue;
            _lastLoggedCategory = int.MinValue;
            _lastCategory = int.MinValue;
            _lastCategoryLabel = null;
            _lastRightHover = int.MinValue;
            _lastRightLock = int.MinValue;
            _lastSpecialFeature = int.MinValue;
            _lastMusicVolume = -1f;
            _lastSoundVolume = -1f;
            _lastAmbientVolume = -1f;
            _lastParallax = int.MinValue;
        }
    }
}
