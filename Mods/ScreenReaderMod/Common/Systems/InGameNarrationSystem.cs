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
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.BigProgressBar;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Gamepad;
using Terraria.UI.Chat;

namespace ScreenReaderMod.Common.Systems;

public sealed class InGameNarrationSystem : ModSystem
{
    private readonly HotbarNarrator _hotbarNarrator = new();
    private readonly SmartCursorNarrator _smartCursorNarrator = new();
    private readonly CraftingNarrator _craftingNarrator = new();
    private readonly CursorNarrator _cursorNarrator = new();
    private readonly InventoryNarrator _inventoryNarrator = new();
    private readonly NpcDialogueNarrator _npcDialogueNarrator = new();
    private readonly IngameSettingsNarrator _ingameSettingsNarrator = new();

    public override void Load()
        {
            if (Main.dedServ)
            {
                return;
            }

            On_ItemSlot.MouseHover_ItemArray_int_int += HandleItemSlotHover;
            On_ItemSlot.MouseHover_refItem_int += HandleItemSlotHoverRef;
            On_Main.DrawNPCChatButtons += CaptureNpcChatButtons;
            On_IngameOptions.Draw += HandleIngameOptionsDraw;
            On_IngameOptions.DrawLeftSide += CaptureIngameOptionsLeft;
            On_IngameOptions.DrawRightSide += CaptureIngameOptionsRight;
        }

        public override void Unload()
        {
            if (Main.dedServ)
        {
            return;
        }

            CursorNarrator.DisposeStaticResources();
            On_ItemSlot.MouseHover_ItemArray_int_int -= HandleItemSlotHover;
            On_ItemSlot.MouseHover_refItem_int -= HandleItemSlotHoverRef;
            On_Main.DrawNPCChatButtons -= CaptureNpcChatButtons;
            On_IngameOptions.Draw -= HandleIngameOptionsDraw;
            On_IngameOptions.DrawLeftSide -= CaptureIngameOptionsLeft;
            On_IngameOptions.DrawRightSide -= CaptureIngameOptionsRight;
        }

    public override void PostUpdatePlayers()
    {
        TryUpdateNarrators(requirePaused: false);
    }

    public override void UpdateUI(GameTime gameTime)
    {
        TryUpdateNarrators(requirePaused: true);
    }

    private void TryUpdateNarrators(bool requirePaused)
    {
        if (Main.dedServ || Main.gameMenu)
        {
            return;
        }

        bool isPaused = Main.gamePaused;
        if (requirePaused)
        {
            if (!isPaused)
            {
                return;
            }
        }
        else if (isPaused)
        {
            return;
        }

        Player player = Main.LocalPlayer;
        if (player is null || !player.active)
        {
            return;
        }

        _hotbarNarrator.Update(player);
        _inventoryNarrator.Update(player);
        _craftingNarrator.Update(player);
        _smartCursorNarrator.Update();
        _cursorNarrator.Update();
        _npcDialogueNarrator.Update(player);
    }

    private static void HandleItemSlotHover(On_ItemSlot.orig_MouseHover_ItemArray_int_int orig, Item[] inv, int context, int slot)
    {
        orig(inv, context, slot);
        InventoryNarrator.RecordFocus(inv, context, slot);
    }

    private static void CaptureNpcChatButtons(On_Main.orig_DrawNPCChatButtons orig, int superColor, Color chatColor, int numLines, string focusText, string focusText3)
    {
        string? primary = focusText;
        string? closeLabel = Lang.inter[52].Value;
        string? secondary = string.IsNullOrWhiteSpace(focusText3) ? null : focusText3;

        string? happiness = null;
        if (!Main.remixWorld)
        {
            int playerIndex = Main.myPlayer;
            if (playerIndex >= 0 && playerIndex < Main.maxPlayers)
            {
                Player? localPlayer = Main.player[playerIndex];
                if (localPlayer is not null && !string.IsNullOrWhiteSpace(localPlayer.currentShoppingSettings.HappinessReport))
                {
                    happiness = Language.GetTextValue("UI.NPCCheckHappiness");
                }
            }
        }

        NpcDialogueNarrator.UpdateButtonLabels(primary, closeLabel, secondary, happiness);
        orig(superColor, chatColor, numLines, focusText, focusText3);
    }

        private void HandleIngameOptionsDraw(On_IngameOptions.orig_Draw orig, Main self, SpriteBatch spriteBatch)
        {
            _ingameSettingsNarrator.PrimeReflection();
            IngameOptionsLabelTracker.BeginFrame();
            orig(self, spriteBatch);
            IngameOptionsLabelTracker.EndFrame();

            if (!Main.gameMenu)
            {
                _ingameSettingsNarrator.Update();
            }
        }

        private static bool CaptureIngameOptionsLeft(On_IngameOptions.orig_DrawLeftSide orig, SpriteBatch spriteBatch, string label, int index, Vector2 anchorPosition, Vector2 offset, float[] scaleArray, float minScale, float maxScale, float scaleSpeed)
        {
            bool result = orig(spriteBatch, label, index, anchorPosition, offset, scaleArray, minScale, maxScale, scaleSpeed);
            IngameOptionsLabelTracker.RecordLeft(index, label);
            return result;
        }

        private static bool CaptureIngameOptionsRight(On_IngameOptions.orig_DrawRightSide orig, SpriteBatch spriteBatch, string label, int index, Vector2 anchorPosition, Vector2 offset, float scale, float lockedScale, Color color)
        {
            bool result = orig(spriteBatch, label, index, anchorPosition, offset, scale, lockedScale, color);
            IngameOptionsLabelTracker.RecordRight(index, label);
            return result;
        }

        private static class IngameOptionsLabelTracker
        {
            private static readonly Dictionary<int, string> LeftLabels = new();
            private static readonly Dictionary<int, int> LeftIndexToCategory = new();
            private static readonly Dictionary<int, string> CategoryLabels = new();
            private static readonly Dictionary<(int category, int optionIndex), string> OptionLabels = new();
            private static bool[] _skipRightSlotSnapshot = Array.Empty<bool>();

            private static FieldInfo? _leftSideCategoryMappingField;
            private static FieldInfo? _skipRightSlotField;
            private static FieldInfo? _categoryField;

            private static readonly Dictionary<int, int> EmptyMapping = new();

            public static void Configure(FieldInfo? leftMapping, FieldInfo? skipField, FieldInfo? categoryField)
            {
                if (leftMapping is not null)
                {
                    _leftSideCategoryMappingField = leftMapping;
                }

                if (skipField is not null)
                {
                    _skipRightSlotField = skipField;
                }

                if (categoryField is not null)
                {
                    _categoryField = categoryField;
                }
            }

            public static void BeginFrame()
            {
                LeftLabels.Clear();
                LeftIndexToCategory.Clear();
                CategoryLabels.Clear();
                OptionLabels.Clear();

                UpdateSkipSnapshot();
            }

            public static void EndFrame()
            {
                UpdateSkipSnapshot();
            }

            public static void RecordLeft(int index, string? label)
            {
                string sanitized = TextSanitizer.Clean(label ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(sanitized))
                {
                    LeftLabels[index] = sanitized;
                }

                foreach (KeyValuePair<int, int> kvp in GetLeftSideCategoryMapping())
                {
                    if (kvp.Key != index)
                    {
                        continue;
                    }

                    LeftIndexToCategory[index] = kvp.Value;
                    if (!string.IsNullOrWhiteSpace(sanitized))
                    {
                        CategoryLabels[kvp.Value] = sanitized;
                    }
                    break;
                }
            }

        public static void RecordRight(int index, string? label)
        {
            UpdateSkipSnapshot();

            int category = GetCurrentCategory();
            if (category < 0)
            {
                return;
            }

            if ((uint)index < (uint)_skipRightSlotSnapshot.Length && _skipRightSlotSnapshot[index])
            {
                return;
            }

            string sanitized = TextSanitizer.Clean(label ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(sanitized))
            {
                OptionLabels[(category, index)] = sanitized;
            }
        }

            public static bool TryGetLeftLabel(int index, out string label)
            {
                return LeftLabels.TryGetValue(index, out label!);
            }

            public static bool TryGetCategoryLabel(int category, out string label)
            {
                return CategoryLabels.TryGetValue(category, out label!);
            }

            public static bool TryMapLeftToCategory(int leftIndex, out int category)
            {
                return LeftIndexToCategory.TryGetValue(leftIndex, out category);
            }

            public static bool TryGetOptionLabel(int category, int optionIndex, out string label)
            {
                return OptionLabels.TryGetValue((category, optionIndex), out label!);
            }

            public static bool IsOptionSkipped(int optionIndex)
            {
                return (uint)optionIndex < (uint)_skipRightSlotSnapshot.Length && _skipRightSlotSnapshot[optionIndex];
            }

            public static IReadOnlyDictionary<int, int> GetLeftMappingSnapshot()
            {
                return LeftIndexToCategory.Count == 0 ? EmptyMapping : new Dictionary<int, int>(LeftIndexToCategory);
            }

            private static void UpdateSkipSnapshot()
            {
                if (_skipRightSlotField?.GetValue(null) is bool[] array)
                {
                    _skipRightSlotSnapshot = array.Length == 0 ? Array.Empty<bool>() : (bool[])array.Clone();
                }
                else
                {
                    _skipRightSlotSnapshot = Array.Empty<bool>();
                }
            }

            private static Dictionary<int, int> GetLeftSideCategoryMapping()
            {
                if (_leftSideCategoryMappingField?.GetValue(null) is Dictionary<int, int> mapping)
                {
                    return mapping;
                }

                return EmptyMapping;
            }

            private static int GetCurrentCategory()
            {
                if (_categoryField?.GetValue(null) is int value)
                {
                    return value;
                }

                return -1;
            }
        }

    private sealed class NpcDialogueNarrator
    {
        private int _lastNpc = -1;
        private string? _lastChat;
        private bool _lastPrimaryFocus;
        private bool _lastCloseFocus;
        private bool _lastSecondaryFocus;
        private bool _lastHappinessFocus;
        private bool _suppressNextButtonAnnouncement;

        private static string? _currentPrimaryButton;
        private static string? _currentCloseButton;
        private static string? _currentSecondaryButton;
        private static string? _currentHappinessButton;

        public void Update(Player player)
        {
            int talkNpc = player.talkNPC;
            bool hasNpc = talkNpc >= 0 && talkNpc < Main.npc.Length;

            if (!hasNpc)
            {
                if (talkNpc == -1)
                {
                    _lastNpc = -1;
                    _lastChat = null;
                    _lastPrimaryFocus = false;
                    _lastCloseFocus = false;
                    _lastSecondaryFocus = false;
                    _lastHappinessFocus = false;
                    _suppressNextButtonAnnouncement = false;
                }

                return;
            }

            NPC npc = Main.npc[talkNpc];
            if (!npc.active)
            {
                _lastNpc = -1;
                _lastChat = null;
                _lastPrimaryFocus = false;
                _lastCloseFocus = false;
                _lastSecondaryFocus = false;
                _lastHappinessFocus = false;
                _suppressNextButtonAnnouncement = false;
                return;
            }

            if (talkNpc != _lastNpc)
            {
                string npcName = npc.GivenOrTypeName;
                if (!string.IsNullOrWhiteSpace(npcName))
                {
                    ScreenReaderService.Announce($"Talking to {npcName}", force: true);
                }

                _lastNpc = talkNpc;
                _lastChat = null;
                _suppressNextButtonAnnouncement = true;
            }

            string chat = Main.npcChatText ?? string.Empty;
            string normalizedText = NormalizeChat(chat);
            if (!string.IsNullOrWhiteSpace(normalizedText) && !string.Equals(normalizedText, _lastChat, StringComparison.Ordinal))
            {
                string prefix = npc.GivenOrTypeName;
                if (!string.IsNullOrWhiteSpace(prefix))
                {
                    ScreenReaderService.Announce($"{prefix} says: {normalizedText}");
                }
                else
                {
                    ScreenReaderService.Announce(normalizedText);
                }

                _lastChat = normalizedText;
                _suppressNextButtonAnnouncement = true;
            }
            else if (string.IsNullOrWhiteSpace(normalizedText))
            {
                _lastChat = null;
                _suppressNextButtonAnnouncement = false;
            }

            HandleButtonFocus(Main.npcChatFocus2, ref _lastPrimaryFocus, _currentPrimaryButton);
            HandleButtonFocus(Main.npcChatFocus1, ref _lastCloseFocus, _currentCloseButton);
            HandleButtonFocus(Main.npcChatFocus3, ref _lastSecondaryFocus, _currentSecondaryButton);
            HandleButtonFocus(Main.npcChatFocus4, ref _lastHappinessFocus, _currentHappinessButton);
        }

        private static string NormalizeChat(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return string.Empty;
            }

            List<TextSnippet> snippets = ChatManager.ParseMessage(rawText, Color.White);
            var collected = new StringBuilder(rawText.Length);

            foreach (TextSnippet snippet in snippets)
            {
                if (!string.IsNullOrWhiteSpace(snippet.Text))
                {
                    collected.Append(snippet.Text);
                }
            }

            if (collected.Length == 0)
            {
                return string.Empty;
            }

            string aggregated = collected.ToString();
            var normalized = new StringBuilder(aggregated.Length);
            bool previousWasWhitespace = false;

            foreach (char character in aggregated)
            {
                if (char.IsWhiteSpace(character))
                {
                    if (!previousWasWhitespace)
                    {
                        normalized.Append(' ');
                        previousWasWhitespace = true;
                    }
                }
                else
                {
                    normalized.Append(character);
                    previousWasWhitespace = false;
                }
            }

            return normalized.ToString().Trim();
        }

        public static void UpdateButtonLabels(string? primary, string? close, string? secondary, string? happiness)
        {
            _currentPrimaryButton = NormalizeLabel(primary);
            _currentCloseButton = NormalizeLabel(close);
            _currentSecondaryButton = NormalizeLabel(secondary);
            _currentHappinessButton = NormalizeLabel(happiness);
        }

        private static string? NormalizeLabel(string? rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return null;
            }

            string normalized = NormalizeChat(rawText);
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private void HandleButtonFocus(bool isFocused, ref bool lastState, string? label)
        {
            if (!isFocused)
            {
                lastState = false;
                return;
            }

            if (!lastState && !string.IsNullOrWhiteSpace(label))
            {
                if (_suppressNextButtonAnnouncement)
                {
                    _suppressNextButtonAnnouncement = false;
                    lastState = true;
                    return;
                }

                string trimmed = label.Trim();
                string announcement = trimmed;
                if (!trimmed.Contains("button", StringComparison.OrdinalIgnoreCase))
                {
                    announcement = $"{trimmed} button";
                }

                ScreenReaderService.Announce(announcement);
            }

            lastState = true;
        }
    }

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
                string? description = DescribeOption(categoryId, rightHover);
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

        private string? DescribeOption(int category, int option)
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

            return DescribeFallback(category, option);
        }

        private static string? DescribeFallback(int category, int option)
        {
            try
            {
                return category switch
                {
                    0 => DescribeGeneral(option),
                    1 => DescribeAudio(option),
                    2 => DescribeInterface(option),
                    3 => DescribeVideo(option),
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

    private static void HandleItemSlotHoverRef(On_ItemSlot.orig_MouseHover_refItem_int orig, ref Item item, int context)
    {
        orig(ref item, context);
        InventoryNarrator.RecordFocus(item, context);
    }

    private sealed class HotbarNarrator
    {
        private int _lastSelectedSlot = -1;
        private int _lastItemType = -1;
        private int _lastPrefix = -1;
        private int _lastStack = -1;

        public void Update(Player player)
        {
            if (ShouldSuppressHotbarNarration(player))
            {
                Reset();
                return;
            }

            int selectedSlot = player.selectedItem;
            Item held = player.HeldItem ?? new Item();

            if (selectedSlot == _lastSelectedSlot &&
                held.type == _lastItemType &&
                held.prefix == _lastPrefix &&
                held.stack == _lastStack)
            {
                return;
            }

            _lastSelectedSlot = selectedSlot;
            _lastItemType = held.type;
            _lastPrefix = held.prefix;
            _lastStack = held.stack;

            string description = DescribeHeldItem(selectedSlot, held);
            if (!string.IsNullOrWhiteSpace(description))
            {
                ScreenReaderService.Announce(description);
            }
        }

        private void Reset()
        {
            _lastSelectedSlot = -1;
            _lastItemType = -1;
            _lastPrefix = -1;
            _lastStack = -1;
        }

        private static bool ShouldSuppressHotbarNarration(Player player)
        {
            int selectedSlot = player.selectedItem;
            if (selectedSlot < 0 || selectedSlot > 9)
            {
                return true;
            }

            bool usingGamepadUi = PlayerInput.UsingGamepadUI;
            if (!usingGamepadUi)
            {
                return false;
            }

            if (InventoryNarrator.IsInventoryUiOpen(player))
            {
                return true;
            }

            int point = UILinkPointNavigator.CurrentPoint;
            return CraftingNarrator.IsCraftingLinkPoint(point);
        }

        private static string DescribeHeldItem(int slot, Item item)
        {
            if (item.IsAir)
            {
                return $"Empty, slot {slot + 1}";
            }

            string label = ComposeItemLabel(item);
            return $"{label}, slot {slot + 1}";
        }
    }

    private sealed class InventoryNarrator
    {
        private ItemIdentity _lastHover;
        private string? _lastHoverLocation;
        private string? _lastHoverTooltip;
        private string? _lastHoverDetails;
        private ItemIdentity _lastMouse;
        private string? _lastMouseMessage;
        private string? _lastEmptyMessage;
        private string? _lastAnnouncedMessage;
        private SlotFocus? _currentFocus;

        private static SlotFocus? _pendingFocus;
        private static readonly Dictionary<int, SlotFocus> LinkPointFocusCache = new();

        private static readonly Lazy<FieldInfo?> MouseTextCacheField = new(() =>
            typeof(Main).GetField("_mouseTextCache", BindingFlags.Instance | BindingFlags.NonPublic));

        private static FieldInfo? _mouseTextCursorField;
        private static FieldInfo? _mouseTextIsValidField;

        public static void RecordFocus(Item[] inventory, int context, int slot)
        {
            SlotFocus focus = new(inventory, null, context, slot);
            CacheLinkPointFocus(focus);

            if (ShouldCaptureFocusForContext(context))
            {
                StorePendingFocus(focus);
            }
        }

        public static void RecordFocus(Item item, int context)
        {
            SlotFocus focus = new(null, item, context, -1);
            CacheLinkPointFocus(focus);

            if (ShouldCaptureFocusForContext(context))
            {
                StorePendingFocus(focus);
            }
        }

        private static bool ShouldCaptureFocusForContext(int context)
        {
            int normalized = Math.Abs(context);
            return normalized != ItemSlot.Context.CraftingMaterial;
        }

        private static void StorePendingFocus(SlotFocus focus)
        {
            _pendingFocus = focus;
        }

        public void Update(Player player)
        {
            if (!IsInventoryUiOpen(player))
            {
                Reset();
                return;
            }

            bool usingGamepad = PlayerInput.UsingGamepadUI;

            SlotFocus? nextFocus = null;
            if (_pendingFocus.HasValue)
            {
                nextFocus = _pendingFocus;
                _pendingFocus = null;
            }
            else if (usingGamepad)
            {
                nextFocus = ResolveFocusFromLinkPoint();
            }

            if (nextFocus.HasValue && IsFocusValid(nextFocus.Value))
            {
                _currentFocus = nextFocus;
            }
            else
            {
                _currentFocus = null;
            }

            HandleMouseItem();
            HandleHoverItem(player);
        }

        internal static bool IsInventoryUiOpen(Player player)
        {
            return Main.playerInventory ||
                   player.chest != -1 ||
                   Main.npcShop != 0 ||
                   Main.InGuideCraftMenu ||
                   Main.InReforgeMenu ||
                   Main.ingameOptionsWindow;
        }

        private void HandleMouseItem()
        {
            Item mouse = Main.mouseItem;
            ItemIdentity identity = ItemIdentity.From(mouse);
            if (identity.IsAir)
            {
                _lastMouse = ItemIdentity.Empty;
                _lastMouseMessage = null;
                return;
            }

            string message = $"Holding {ComposeItemLabel(mouse)}";
            if (identity.Equals(_lastMouse) && string.Equals(message, _lastMouseMessage, StringComparison.Ordinal))
            {
                return;
            }

            _lastMouse = identity;
            _lastMouseMessage = message;
            ScreenReaderService.Announce(message);
        }

        private void HandleHoverItem(Player player)
        {
            SlotFocus? focus = _currentFocus;
            Item? focusedItem = GetItemFromFocus(focus);
            bool usingGamepadFocus = PlayerInput.UsingGamepadUI && focusedItem is not null;

            Item hover = usingGamepadFocus ? focusedItem! : Main.HoverItem;
            ItemIdentity identity = ItemIdentity.From(hover);

            string rawTooltip;
            if (usingGamepadFocus)
            {
                rawTooltip = GetHoverNameForItem(hover);
            }
            else
            {
                rawTooltip = Main.hoverItemName ?? string.Empty;
            }
            string normalizedTooltip = GlyphTagFormatter.Normalize(rawTooltip);

            string location = DescribeLocation(player, identity, focus);

            if (TryAnnounceSpecialSelection(identity.IsAir, location))
            {
                return;
            }

            if (!identity.IsAir)
            {
                string label = ComposeItemLabel(hover);
                string message = string.IsNullOrEmpty(location) ? label : $"{label}, {location}";
                string? details = BuildTooltipDetails(hover, rawTooltip, allowMouseText: !usingGamepadFocus);
                string? requirementDetails = CraftingNarrator.TryGetRequirementTooltipDetails(hover, string.IsNullOrWhiteSpace(location));
                if (!string.IsNullOrWhiteSpace(requirementDetails))
                {
                    details = string.IsNullOrWhiteSpace(details)
                        ? requirementDetails
                        : $"{details}. {requirementDetails}";
                }
                string combined = CombineItemAnnouncement(message, details);

                if (identity.Equals(_lastHover) &&
                    string.Equals(combined, _lastAnnouncedMessage, StringComparison.Ordinal) &&
                    string.Equals(normalizedTooltip, _lastHoverTooltip, StringComparison.Ordinal))
                {
                    return;
                }

                _lastHover = identity;
                _lastHoverLocation = location;
                _lastHoverTooltip = normalizedTooltip;
                _lastHoverDetails = details;
                _lastEmptyMessage = null;
                _lastAnnouncedMessage = combined;

                ScreenReaderService.Announce(combined);
                return;
            }

            _lastHover = ItemIdentity.Empty;
            _lastHoverDetails = null;

            if (!string.IsNullOrEmpty(location))
            {
                string message = $"Empty, {location}";

                if (!string.Equals(message, _lastEmptyMessage, StringComparison.Ordinal))
                {
                    _lastEmptyMessage = message;
                    _lastHoverLocation = location;
                    _lastHoverTooltip = null;
                    _lastHoverDetails = null;
                    _lastAnnouncedMessage = message;
                    ScreenReaderService.Announce(message);
                }

                return;
            }

            if (usingGamepadFocus)
            {
                return;
            }

            string? mouseText = TryGetMouseText();
            if (!string.IsNullOrWhiteSpace(mouseText))
            {
                string trimmedMouseText = GlyphTagFormatter.Normalize(mouseText.Trim());
                if (!string.Equals(trimmedMouseText, _lastHoverTooltip, StringComparison.Ordinal))
                {
                    _lastHoverTooltip = trimmedMouseText;
                    _lastHoverLocation = null;
                    _lastEmptyMessage = null;
                    _lastHoverDetails = null;
                    _lastAnnouncedMessage = trimmedMouseText;
                    ScreenReaderService.Announce(trimmedMouseText);
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(rawTooltip))
            {
                if (!string.Equals(normalizedTooltip, _lastHoverTooltip, StringComparison.Ordinal))
                {
                    _lastHoverTooltip = normalizedTooltip;
                    _lastHoverLocation = null;
                    _lastEmptyMessage = null;
                    _lastHoverDetails = null;
                    _lastAnnouncedMessage = normalizedTooltip;
                    ScreenReaderService.Announce(normalizedTooltip);
                }
            }
        }

        private static void CacheLinkPointFocus(SlotFocus focus)
        {
            if (!PlayerInput.UsingGamepadUI)
            {
                return;
            }

            int point = UILinkPointNavigator.CurrentPoint;
            if (point < 0)
            {
                return;
            }

            LinkPointFocusCache[point] = focus;
        }

        private static SlotFocus? ResolveFocusFromLinkPoint()
        {
            int point = UILinkPointNavigator.CurrentPoint;
            if (point < 0)
            {
                return null;
            }

            if (!LinkPointFocusCache.TryGetValue(point, out SlotFocus focus))
            {
                return null;
            }

            if (!ShouldCaptureFocusForContext(focus.Context))
            {
                return null;
            }

            if (IsFocusValid(focus))
            {
                return focus;
            }

            LinkPointFocusCache.Remove(point);
            return null;
        }

        public static bool TryGetContextForLinkPoint(int point, out int context)
        {
            if (point >= 0 && LinkPointFocusCache.TryGetValue(point, out SlotFocus focus))
            {
                context = focus.Context;
                return true;
            }

            context = -1;
            return false;
        }

        public static bool TryGetItemForLinkPoint(int point, out Item? item, out int context)
        {
            item = null;
            context = -1;

            if (point < 0)
            {
                return false;
            }

            if (!LinkPointFocusCache.TryGetValue(point, out SlotFocus focus))
            {
                return false;
            }

            context = focus.Context;

            if (focus.Items is Item[] items)
            {
                int index = focus.Slot;
                if ((uint)index < (uint)items.Length)
                {
                    item = items[index];
                }
            }
            else
            {
                item = focus.SingleItem;
            }

            if (item is null || item.IsAir)
            {
                item = null;
                return false;
            }

            return true;
        }

        private static bool IsFocusValid(SlotFocus focus)
        {
            if (focus.Items is Item[] items)
            {
                int index = focus.Slot;
                return (uint)index < (uint)items.Length;
            }

            return focus.SingleItem is not null;
        }

        private static Item? GetItemFromFocus(SlotFocus? focus)
        {
            if (!focus.HasValue)
            {
                return null;
            }

            SlotFocus value = focus.Value;

            if (value.Items is Item[] items)
            {
                int index = value.Slot;
                if ((uint)index < (uint)items.Length)
                {
                    return items[index];
                }

                return null;
            }

            return value.SingleItem;
        }

        private static string GetHoverNameForItem(Item item)
        {
            string name = item.AffixName();
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            if (!string.IsNullOrWhiteSpace(item.Name))
            {
                return item.Name;
            }

            string fallback = Lang.GetItemNameValue(item.type);
            return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback;
        }

        private void Reset()
        {
            _lastHover = ItemIdentity.Empty;
            _lastHoverLocation = null;
            _lastHoverTooltip = null;
            _lastHoverDetails = null;
            _lastMouse = ItemIdentity.Empty;
            _lastMouseMessage = null;
            _lastEmptyMessage = null;
            _lastAnnouncedMessage = null;
            _currentFocus = null;
            _pendingFocus = null;
            LinkPointFocusCache.Clear();
        }

        private static string DescribeLocation(Player player, ItemIdentity identity, SlotFocus? focus)
        {
            if (focus.HasValue)
            {
                string focused = DescribeFocusedSlot(player, focus.Value);
                if (!string.IsNullOrWhiteSpace(focused))
                {
                    return focused;
                }
            }

            if (TryMatch(player.inventory, identity, out int inventoryIndex))
            {
                if (inventoryIndex < 10)
                {
                    return $"Hotbar slot {inventoryIndex + 1}";
                }

                if (inventoryIndex < 50)
                {
                    return $"Inventory slot {inventoryIndex - 9}";
                }

                if (inventoryIndex < 54)
                {
                    return $"Coin slot {inventoryIndex - 49}";
                }

                if (inventoryIndex < 58)
                {
                    return $"Ammo slot {inventoryIndex - 53}";
                }
            }

            if (Matches(player.trashItem, identity))
            {
                return "Trash slot";
            }

            if (TryMatch(player.armor, identity, out int armorIndex))
            {
                return DescribeArmorSlot(armorIndex);
            }

            if (TryMatch(player.dye, identity, out int dyeIndex))
            {
                return $"Dye slot {dyeIndex + 1}";
            }

            if (TryMatch(player.miscEquips, identity, out int miscIndex))
            {
                return $"Misc equipment slot {miscIndex + 1}";
            }

            if (TryMatch(player.miscDyes, identity, out int miscDyeIndex))
            {
                return $"Misc dye slot {miscDyeIndex + 1}";
            }

            int chestIndex = player.chest;
            if (chestIndex != -1)
            {
                string container = DescribeContainer(chestIndex);
                Item[]? containerItems = GetContainerItems(player, chestIndex);
                if (containerItems is not null && TryMatch(containerItems, identity, out int containerSlot))
                {
                    return $"{container} slot {containerSlot + 1}";
                }

                return container;
            }

            if (Main.npcShop > 0)
            {
                Chest[]? shops = Main.instance?.shop;
                if (shops is not null && Main.npcShop < shops.Length)
                {
                    Item[]? shopItems = shops[Main.npcShop]?.item;
                    if (shopItems is not null && TryMatch(shopItems, identity, out int shopSlot))
                    {
                        return $"Shop slot {shopSlot + 1}";
                    }
                }
            }

            return string.Empty;
        }

        private static string DescribeFocusedSlot(Player player, SlotFocus focus)
        {
            if (focus.Items is Item[] items)
            {
                if (ReferenceEquals(items, player.inventory))
                {
                    return DescribeInventorySlot(focus.Slot);
                }

                if (ReferenceEquals(items, player.bank.item))
                {
                    return $"Piggy bank slot {focus.Slot + 1}";
                }

                if (ReferenceEquals(items, player.bank2.item))
                {
                    return $"Safe slot {focus.Slot + 1}";
                }

                if (ReferenceEquals(items, player.bank3.item))
                {
                    return $"Defender's forge slot {focus.Slot + 1}";
                }

                if (ReferenceEquals(items, player.bank4.item))
                {
                    return $"Void vault slot {focus.Slot + 1}";
                }

                if (ReferenceEquals(items, player.armor))
                {
                    return DescribeArmorSlot(focus.Slot);
                }

                if (ReferenceEquals(items, player.dye))
                {
                    return $"Dye slot {focus.Slot + 1}";
                }

                if (ReferenceEquals(items, player.miscEquips))
                {
                    return $"Misc equipment slot {focus.Slot + 1}";
                }

                if (ReferenceEquals(items, player.miscDyes))
                {
                    return $"Misc dye slot {focus.Slot + 1}";
                }

                if (Main.chest is not null)
                {
                    for (int i = 0; i < Main.chest.Length; i++)
                    {
                        if (ReferenceEquals(Main.chest[i]?.item, items))
                        {
                            string container = DescribeContainer(i);
                            return focus.Slot >= 0 ? $"{container} slot {focus.Slot + 1}" : container;
                        }
                    }
                }

                Chest[]? shops = Main.instance?.shop;
                if (shops is not null)
                {
                    for (int i = 0; i < shops.Length; i++)
                    {
                        if (ReferenceEquals(shops[i]?.item, items))
                        {
                            return focus.Slot >= 0 ? $"Shop slot {focus.Slot + 1}" : "Shop slot";
                        }
                    }
                }
            }

            int context = Math.Abs(focus.Context);

            if (context == ItemSlot.Context.TrashItem)
            {
                return "Trash slot";
            }

            return string.Empty;
        }

        private static string DescribeInventorySlot(int slot)
        {
            if (slot < 0)
            {
                return string.Empty;
            }

            if (slot < 10)
            {
                return $"Hotbar slot {slot + 1}";
            }

            if (slot < 50)
            {
                return $"Inventory slot {slot - 9}";
            }

            if (slot < 54)
            {
                return $"Coin slot {slot - 49}";
            }

            if (slot < 58)
            {
                return $"Ammo slot {slot - 53}";
            }

            return string.Empty;
        }

        private static Item[]? GetContainerItems(Player player, int chestIndex)
        {
            if (chestIndex >= 0 && chestIndex < Main.chest.Length)
            {
                return Main.chest[chestIndex]?.item;
            }

            return chestIndex switch
            {
                -2 => player.bank.item,
                -3 => player.bank2.item,
                -4 => player.bank3.item,
                -5 => player.bank4.item,
                _ => null,
            };
        }

        private static bool TryMatch(Item[] items, ItemIdentity identity, out int index)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (Matches(items[i], identity))
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        private static bool Matches(Item item, ItemIdentity identity)
        {
            if (item is null || item.IsAir)
            {
                return false;
            }

            return ItemIdentity.From(item).Equals(identity);
        }

        private static string DescribeContainer(int chestIndex)
        {
            return chestIndex switch
            {
                >= 0 => "Chest",
                -2 => "Piggy bank",
                -3 => "Safe",
                -4 => "Defender's forge",
                -5 => "Void vault",
                _ => "Chest",
            };
        }

        private static string DescribeArmorSlot(int index)
        {
            return index switch
            {
                0 => "Helmet slot",
                1 => "Chestplate slot",
                2 => "Leggings slot",
                >= 3 and < 10 => $"Accessory slot {index - 2}",
                >= 10 and < 13 => $"Vanity armor slot {index - 9}",
                >= 13 and < 20 => $"Vanity accessory slot {index - 12}",
                _ => $"Armor slot {index + 1}",
            };
        }

        private static string? TryGetMouseText()
        {
            Main? main = Main.instance;
            if (main is null)
            {
                return null;
            }

            FieldInfo? cacheField = MouseTextCacheField.Value;
            if (cacheField is null)
            {
                return null;
            }

            object? cache = cacheField.GetValue(main);
            if (cache is null)
            {
                return null;
            }

            Type cacheType = cache.GetType();
            _mouseTextCursorField ??= cacheType.GetField("cursorText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _mouseTextIsValidField ??= cacheType.GetField("isValid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (_mouseTextIsValidField is not null && _mouseTextIsValidField.GetValue(cache) is bool isValid && !isValid)
            {
                return null;
            }

            if (_mouseTextCursorField?.GetValue(cache) is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }

            return null;
        }

        internal static string? BuildTooltipDetails(Item item, string hoverName, bool allowMouseText = true, bool suppressControllerPrompts = false)
        {
            if (item is null || item.IsAir)
            {
                return null;
            }

            HashSet<string> nameCandidates = BuildItemNameCandidates(item, hoverName);
            List<string>? lines = null;
            if (allowMouseText)
            {
                string? raw = TryGetMouseText();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    lines = ExtractTooltipLines(raw, nameCandidates, suppressControllerPrompts);
                }
            }

            if (lines is null || lines.Count == 0)
            {
                lines = ExtractTooltipLinesFromItem(item, nameCandidates, suppressControllerPrompts);
            }

            if (lines.Count == 0)
            {
                return null;
            }

            string formatted = FormatTooltipLines(lines);
            return string.IsNullOrWhiteSpace(formatted) ? null : formatted;
        }

        private static List<string> ExtractTooltipLines(string raw, HashSet<string> nameCandidates, bool suppressControllerPrompts)
        {
            string sanitized = GlyphTagFormatter.SanitizeTooltip(raw);
            List<string> lines = new();

            string[] segments = sanitized.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string segment in segments)
            {
                string trimmed = segment.Trim();
                if (trimmed.Length > 0 &&
                    !IsItemNameLine(trimmed, nameCandidates) &&
                    (!suppressControllerPrompts || !ShouldRemoveControllerPromptLine(trimmed)))
                {
                    lines.Add(trimmed);
                }
            }

            return lines;
        }

        private static List<string> ExtractTooltipLinesFromItem(Item item, HashSet<string> nameCandidates, bool suppressControllerPrompts)
        {
            List<string> lines = new();

            try
            {
                Item clone = item.Clone();
                const int MaxLines = 60;
                string[] toolTipLine = new string[MaxLines];
                bool[] preFixLine = new bool[MaxLines];
                bool[] badPreFixLine = new bool[MaxLines];
                string[] toolTipNames = new string[MaxLines];
                int yoyoLogo = -1;
                int researchLine = -1;
                float originalKnockBack = clone.knockBack;
                int numLines = 1;

                Main.MouseText_DrawItemTooltip_GetLinesInfo(
                    clone,
                    ref yoyoLogo,
                    ref researchLine,
                    originalKnockBack,
                    ref numLines,
                    toolTipLine,
                    preFixLine,
                    badPreFixLine,
                    toolTipNames,
                    out _);

                for (int i = 0; i < numLines && i < toolTipLine.Length; i++)
                {
                    string? line = toolTipLine[i];
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    string? entryName = toolTipNames[i];
                    if (!string.IsNullOrWhiteSpace(entryName) &&
                        string.Equals(entryName, "ItemName", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string trimmed = line.Trim();
                    if (IsItemNameLine(trimmed, nameCandidates))
                    {
                        continue;
                    }

                    if (suppressControllerPrompts && ShouldRemoveControllerPromptLine(trimmed))
                    {
                        continue;
                    }

                    lines.Add(trimmed);
                }
            }
            catch
            {
                // Swallow exceptions and return whatever we have.
            }

            return lines;
        }

        private static bool IsItemNameLine(string? line, HashSet<string> nameCandidates)
        {
            if (string.IsNullOrWhiteSpace(line) || nameCandidates is null || nameCandidates.Count == 0)
            {
                return false;
            }

            string normalizedLine = GlyphTagFormatter.Normalize(line).Trim();
            return nameCandidates.Contains(normalizedLine);
        }

        private static bool ShouldRemoveControllerPromptLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string normalized = GlyphTagFormatter.Normalize(line).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            string lower = normalized.ToLowerInvariant();
            if (!lower.Contains("craft", StringComparison.Ordinal))
            {
                return false;
            }

            if (lower.Contains("right bumper", StringComparison.Ordinal) ||
                lower.Contains("left bumper", StringComparison.Ordinal) ||
                lower.Contains("right trigger", StringComparison.Ordinal) ||
                lower.Contains("left trigger", StringComparison.Ordinal) ||
                lower.Contains("button", StringComparison.Ordinal) ||
                lower.Contains("bumper", StringComparison.Ordinal) ||
                lower.Contains("trigger", StringComparison.Ordinal) ||
                lower.Contains("gamepad", StringComparison.Ordinal) ||
                lower.Contains("controller", StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        private static HashSet<string> BuildItemNameCandidates(Item item, string hoverName)
        {
            HashSet<string> candidates = new(StringComparer.OrdinalIgnoreCase);
            AddCandidate(candidates, hoverName);
            AddCandidate(candidates, item.Name);
            AddCandidate(candidates, item.AffixName());
            AddCandidate(candidates, Lang.GetItemNameValue(item.type));
            return candidates;
        }

        private static void AddCandidate(HashSet<string> candidates, string? value)
        {
            if (candidates is null)
            {
                return;
            }

            string normalized = GlyphTagFormatter.NormalizeNameCandidate(value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                candidates.Add(normalized);
            }
        }

        internal static string CombineItemAnnouncement(string message, string? details)
        {
            string normalizedMessage = GlyphTagFormatter.Normalize(message.Trim());
            if (string.IsNullOrWhiteSpace(details))
            {
                return normalizedMessage;
            }

            string normalizedDetails = GlyphTagFormatter.Normalize(details.Trim());
            if (!HasTerminalPunctuation(normalizedMessage))
            {
                normalizedMessage += ".";
            }

            string combined = $"{normalizedMessage} {normalizedDetails}";
            return combined;
        }

        private static string FormatTooltipLines(List<string> lines)
        {
            StringBuilder builder = new();
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                if (line.EndsWith(":", StringComparison.Ordinal))
                {
                    builder.Append(line);
                    if (i + 1 < lines.Count)
                    {
                        string next = lines[++i];
                        if (!string.IsNullOrWhiteSpace(next))
                        {
                            builder.Append(' ');
                            builder.Append(next);
                            if (!HasTerminalPunctuation(next))
                            {
                                builder.Append('.');
                            }
                        }
                    }

                    continue;
                }

                builder.Append(line);
                if (!HasTerminalPunctuation(line))
                {
                    builder.Append('.');
                }
            }

            string result = builder.ToString();
            return GlyphTagFormatter.Normalize(result);
        }

        private static bool HasTerminalPunctuation(string text)
        {
            text = text.TrimEnd();
            if (text.Length == 0)
            {
                return false;
            }

            char last = text[^1];
            return last == '.' || last == '!' || last == '?' || last == ':' || last == ')';
        }

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

        private readonly struct SlotFocus
        {
            public SlotFocus(Item[]? items, Item? singleItem, int context, int slot)
            {
                Items = items;
                SingleItem = singleItem;
                Context = context;
                Slot = slot;
            }

            public Item[]? Items { get; }
            public Item? SingleItem { get; }
            public int Context { get; }
            public int Slot { get; }
        }

        private readonly struct ItemIdentity : IEquatable<ItemIdentity>
        {
            public static ItemIdentity Empty => default;

            public ItemIdentity(int type, int prefix, int stack, bool favorited)
            {
                Type = type;
                Prefix = prefix;
                Stack = stack;
                Favorited = favorited;
            }

            public int Type { get; }
            public int Prefix { get; }
            public int Stack { get; }
            public bool Favorited { get; }

            public bool IsAir => Type <= 0 || Stack <= 0;

            public static ItemIdentity From(Item item)
            {
                if (item is null || item.IsAir)
                {
                    return Empty;
                }

                return new ItemIdentity(item.type, item.prefix, item.stack, item.favorited);
            }

            public bool Equals(ItemIdentity other)
            {
                return Type == other.Type &&
                       Prefix == other.Prefix &&
                       Stack == other.Stack &&
                       Favorited == other.Favorited;
            }

            public override bool Equals(object? obj)
            {
                return obj is ItemIdentity other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Type, Prefix, Stack, Favorited);
            }
        }
    }

    private sealed class CraftingNarrator
    {
        private int _lastFocusIndex = -1;
        private int _lastRecipeIndex = -1;
        private string? _lastAnnouncement;
        private int _lastCount = -1;
        private string? _lastRequirementsMessage;
        private int _lastIngredientPoint = -1;
        private int _lastIngredientRecipeIndex = -1;
        private IngredientIdentity _lastIngredientIdentity = IngredientIdentity.Empty;
        private string? _lastIngredientMessage;
        private readonly HashSet<int> _missingRequirementRecipes = new();

        private static readonly FieldInfo[] CraftingShortcutFields = typeof(UILinkPointNavigator.Shortcuts)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(int) &&
                            field.Name.Contains("CRAFT", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        private static readonly FieldInfo[] CraftingLinkIdFields = DiscoverCraftingLinkIdFields();

        private static readonly Lazy<int[]> CraftingContextValues = new(DiscoverCraftingContexts);
        private static readonly Lazy<Dictionary<int, int>> RecipeGroupLookup = new(DiscoverRecipeGroupLookup);
        private static readonly Func<Recipe, int, int>? AcceptedGroupResolver = CreateAcceptedGroupResolver();

        private static bool _loggedShortcutReflectionWarning;
        private static bool _loggedRecipeGroupReflectionWarning;
        private static bool _loggedRecipeFlagReflectionWarning;

        private readonly struct IngredientIdentity : IEquatable<IngredientIdentity>
        {
            public static IngredientIdentity Empty => default;

            public IngredientIdentity(int type, int prefix, int stack)
            {
                Type = type;
                Prefix = prefix;
                Stack = stack;
            }

            public int Type { get; }
            public int Prefix { get; }
            public int Stack { get; }

            public bool IsAir => Type <= 0 || Stack <= 0;

            public static IngredientIdentity From(Item item)
            {
                if (item is null || item.IsAir)
                {
                    return Empty;
                }

                return new IngredientIdentity(item.type, item.prefix, item.stack);
            }

            public bool Equals(IngredientIdentity other)
            {
                return Type == other.Type &&
                       Prefix == other.Prefix &&
                       Stack == other.Stack;
            }

            public override bool Equals(object? obj)
            {
                return obj is IngredientIdentity other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Type, Prefix, Stack);
            }
        }

        private static readonly string[] NeedWaterMembers = { "needWater", "_needWater", "NeedWater" };
        private static readonly string[] NeedHoneyMembers = { "needHoney", "_needHoney", "NeedHoney" };
        private static readonly string[] NeedLavaMembers = { "needLava", "_needLava", "NeedLava" };
        private static readonly string[] NeedSnowBiomeMembers = { "needSnowBiome", "_needSnowBiome", "NeedSnowBiome" };
        private static readonly string[] NeedGraveyardBiomeMembers = { "needGraveyardBiome", "_needGraveyardBiome", "NeedGraveyardBiome" };
        private static readonly string[] AnyIronBarMembers = { "anyIronBar", "_anyIronBar", "AnyIronBar" };
        private static readonly string[] AnyWoodMembers = { "anyWood", "_anyWood", "AnyWood" };
        private static readonly string[] AnySandMembers = { "anySand", "_anySand", "AnySand" };
        private static readonly string[] AnyFragmentMembers = { "anyFragment", "_anyFragment", "AnyFragment" };
        private static readonly string[] AnyPressurePlateMembers = { "anyPressurePlate", "_anyPressurePlate", "AnyPressurePlate" };

        private static bool TryGetFocusedRecipe(out Recipe recipe, out int recipeIndex, out int focusIndex, out int availableCount)
        {
            recipe = null!;
            recipeIndex = -1;
            focusIndex = -1;
            availableCount = Math.Clamp(Main.numAvailableRecipes, 0, Main.availableRecipe.Length);
            if (availableCount <= 0)
            {
                return false;
            }

            focusIndex = Utils.Clamp(Main.focusRecipe, 0, availableCount - 1);
            if (focusIndex < 0 || focusIndex >= Main.availableRecipe.Length)
            {
                return false;
            }

            recipeIndex = Main.availableRecipe[focusIndex];
            if (recipeIndex < 0 || recipeIndex >= Main.recipe.Length)
            {
                return false;
            }

            Recipe candidate = Main.recipe[recipeIndex];
            if (candidate is null || candidate.createItem is null || candidate.createItem.IsAir)
            {
                return false;
            }

            recipe = candidate;
            return true;
        }

        private static bool IsRecipeResultItem(Recipe recipe, Item item)
        {
            if (recipe is null || item is null || item.IsAir)
            {
                return false;
            }

            Item result = recipe.createItem;
            if (result is null || result.IsAir)
            {
                return false;
            }

            if (item.type != result.type)
            {
                return false;
            }

            IngredientIdentity candidate = IngredientIdentity.From(item);
            IngredientIdentity target = IngredientIdentity.From(result);
            return candidate.Equals(target);
        }

        internal static string? TryGetRequirementTooltipDetails(Item item, bool locationIsEmpty)
        {
            if (item is null || item.IsAir)
            {
                return null;
            }

            if (!locationIsEmpty)
            {
                return null;
            }

            if (!Main.playerInventory)
            {
                return null;
            }

            if (!Main.mouseItem.IsAir)
            {
                return null;
            }

            if (!TryGetFocusedRecipe(out Recipe recipe, out _, out _, out _))
            {
                return null;
            }

            if (!IsRecipeResultItem(recipe, item))
            {
                return null;
            }

            string? message = BuildRequirementMessage(recipe, out _);
            return string.IsNullOrWhiteSpace(message) ? null : message;
        }

        public void Update(Player player)
        {
            if (!InventoryNarrator.IsInventoryUiOpen(player))
            {
                Reset();
                return;
            }

            bool usingGamepadUi = PlayerInput.UsingGamepadUI;
            int currentPoint = usingGamepadUi ? UILinkPointNavigator.CurrentPoint : -1;

            if (!usingGamepadUi)
            {
                ResetIngredientFocus();
            }

            if (usingGamepadUi)
            {
                if (InventoryNarrator.TryGetContextForLinkPoint(currentPoint, out int context) &&
                    !IsCraftingContext(context))
                {
                    ResetFocus();
                    return;
                }

                if (!IsCraftingLinkPoint(currentPoint))
                {
                    ResetFocus();
                    return;
                }
            }

            if (!TryGetFocusedRecipe(out Recipe recipe, out int recipeIndex, out int focus, out int available))
            {
                ResetFocus();
                return;
            }

            Item result = recipe.createItem;

            string label = ComposeItemLabel(result);
            if (result.stack > 1)
            {
                label = $"{result.stack} {label}";
            }

            string? details = InventoryNarrator.BuildTooltipDetails(
                result,
                result.Name ?? string.Empty,
                allowMouseText: false,
                suppressControllerPrompts: true);

            int ingredientCount = CountIngredients(recipe);
            string? requirementMessage = BuildRequirementMessage(recipe, out bool hadRequirementData);
            if (!string.IsNullOrWhiteSpace(requirementMessage))
            {
                details = string.IsNullOrWhiteSpace(details)
                    ? requirementMessage
                    : $"{details}. {requirementMessage}";
                _missingRequirementRecipes.Remove(recipeIndex);
            }
            else if (hadRequirementData && _missingRequirementRecipes.Add(recipeIndex))
            {
                ScreenReaderMod.Instance?.Logger.Debug($"[CraftingNarrator] Requirement narration missing for recipe {recipeIndex} ({label})");
            }

            string combined = InventoryNarrator.CombineItemAnnouncement(label, details);
            if (string.IsNullOrWhiteSpace(combined))
            {
                combined = label;
            }

            string itemMessage = $"{combined}. Recipe {focus + 1} of {available}";
            itemMessage = GlyphTagFormatter.Normalize(itemMessage);

            bool ingredientFocused = usingGamepadUi && TryAnnounceIngredientFocus(recipe, recipeIndex, ingredientCount, currentPoint);

            bool itemChanged =
                focus != _lastFocusIndex ||
                recipeIndex != _lastRecipeIndex ||
                available != _lastCount ||
                !string.Equals(itemMessage, _lastAnnouncement, StringComparison.Ordinal);

            _lastRequirementsMessage = requirementMessage;

            if (!itemChanged)
            {
                return;
            }

            _lastFocusIndex = focus;
            _lastRecipeIndex = recipeIndex;
            _lastAnnouncement = itemMessage;
            _lastCount = available;

            if (ingredientFocused)
            {
                return;
            }

            ScreenReaderService.Announce(itemMessage, force: true);
        }

        public static bool IsCraftingLinkPoint(int point)
        {
            if (point < 0)
            {
                return false;
            }

            if (MatchesCraftingField(point, CraftingShortcutFields))
            {
                return true;
            }

            if (MatchesCraftingField(point, CraftingLinkIdFields))
            {
                return true;
            }

            return false;
        }

        private static bool IsCraftingContext(int context)
        {
            int normalized = Math.Abs(context);
            foreach (int value in CraftingContextValues.Value)
            {
                if (normalized == value)
                {
                    return true;
                }
            }

            return normalized == Math.Abs(ItemSlot.Context.CraftingMaterial);
        }

        private void Reset()
        {
            ResetFocus();
            _lastAnnouncement = null;
            _lastRequirementsMessage = null;
            _missingRequirementRecipes.Clear();
        }

        private void ResetFocus()
        {
            _lastFocusIndex = -1;
            _lastRecipeIndex = -1;
            _lastCount = -1;
            _lastRequirementsMessage = null;
            ResetIngredientFocus();
        }

        private void ResetIngredientFocus()
        {
            _lastIngredientPoint = -1;
            _lastIngredientRecipeIndex = -1;
            _lastIngredientIdentity = IngredientIdentity.Empty;
            _lastIngredientMessage = null;
        }

        private static int[] DiscoverCraftingContexts()
        {
            try
            {
                FieldInfo[] fields = typeof(ItemSlot.Context)
                    .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                if (fields.Length == 0)
                {
                    return new[] { Math.Abs(ItemSlot.Context.CraftingMaterial) };
                }

                return fields
                    .Where(field => field.FieldType == typeof(int) &&
                                    field.Name.Contains("CRAFT", StringComparison.OrdinalIgnoreCase))
                    .Select(field =>
                    {
                        try
                        {
                            return Math.Abs((int)(field.GetValue(null) ?? -1));
                        }
                        catch
                        {
                            return -1;
                        }
                    })
                    .Where(value => value >= 0)
                    .Distinct()
                    .ToArray();
            }
            catch
            {
                return new[] { Math.Abs(ItemSlot.Context.CraftingMaterial) };
            }
        }

        private static FieldInfo[] DiscoverCraftingLinkIdFields()
        {
            Type? linkIdType = typeof(UILinkPointNavigator).Assembly.GetType("Terraria.UI.Gamepad.UILinkPointID");
            if (linkIdType is null)
            {
                return Array.Empty<FieldInfo>();
            }

            return linkIdType
                .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)
                .Where(field => field.FieldType == typeof(int) &&
                                (field.Name.Contains("CRAFT", StringComparison.OrdinalIgnoreCase) ||
                                 field.Name.Contains("RECIPE", StringComparison.OrdinalIgnoreCase)))
                .ToArray();
        }

        private static bool MatchesCraftingField(int point, FieldInfo[] fields)
        {
            foreach (FieldInfo field in fields)
            {
                try
                {
                    object? value = field.GetValue(null);
                    if (value is int intValue && intValue >= 0 && intValue == point)
                    {
                        return true;
                    }
                }
                catch (Exception ex) when (!_loggedShortcutReflectionWarning)
                {
                    _loggedShortcutReflectionWarning = true;
                    ScreenReaderMod.Instance?.Logger.Warn($"[CraftingNarrator] Failed to inspect crafting link field {field.Name}: {ex}");
                }
            }

            return false;
        }

        private static string? BuildRequirementMessage(Recipe recipe, out bool hadRequirements)
        {
            hadRequirements = false;
            if (recipe is null)
            {
                return null;
            }

            List<string> ingredientParts = BuildIngredientRequirementParts(recipe);
            List<string> stationParts = BuildStationRequirementParts(recipe);

            hadRequirements = ingredientParts.Count > 0 || stationParts.Count > 0;
            if (!hadRequirements)
            {
                return null;
            }

            List<string> segments = new();
            if (ingredientParts.Count > 0)
            {
                segments.Add($"Requires {string.Join(", ", ingredientParts)}");
            }

            if (stationParts.Count > 0)
            {
                string prefix = TextSanitizer.Clean(Lang.inter[22].Value);
                if (string.IsNullOrWhiteSpace(prefix))
                {
                    prefix = "Required objects:";
                }

                segments.Add($"{prefix} {string.Join(", ", stationParts)}");
            }

            string message = string.Join(". ", segments);
            return GlyphTagFormatter.Normalize(message);
        }

        private static List<string> BuildIngredientRequirementParts(Recipe recipe)
        {
            var parts = new List<string>();
            IList<Item>? requiredItems = recipe.requiredItem;
            if (requiredItems is null || requiredItems.Count == 0)
            {
                return parts;
            }

            for (int i = 0; i < requiredItems.Count; i++)
            {
                Item ingredient = requiredItems[i];
                if (ingredient is null)
                {
                    continue;
                }

                if (ingredient.type == 0)
                {
                    break;
                }

                if (ingredient.IsAir || ingredient.stack <= 0)
                {
                    continue;
                }

                string? description = DescribeRequirement(recipe, ingredient, i);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    parts.Add(description);
                }
            }

            return parts;
        }

        private static List<string> BuildStationRequirementParts(Recipe recipe)
        {
            var results = new List<string>();
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddUnique(string? value)
            {
                string cleaned = TextSanitizer.Clean(value);
                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    return;
                }

                string normalized = GlyphTagFormatter.Normalize(cleaned);
                if (unique.Add(normalized))
                {
                    results.Add(normalized);
                }
            }

            IList<int>? requiredTiles = recipe.requiredTile;
            if (requiredTiles is not null)
            {
                for (int i = 0; i < requiredTiles.Count; i++)
                {
                    int tileId = requiredTiles[i];
                    if (tileId == -1)
                    {
                        break;
                    }

                    AddUnique(ResolveRequiredTileLabel(tileId));
                }
            }

            if (TryGetRecipeBool(recipe, NeedWaterMembers, "needWater", out bool needWater) && needWater)
            {
                AddUnique(Lang.inter[53].Value);
            }

            if (TryGetRecipeBool(recipe, NeedHoneyMembers, "needHoney", out bool needHoney) && needHoney)
            {
                AddUnique(Lang.inter[58].Value);
            }

            if (TryGetRecipeBool(recipe, NeedLavaMembers, "needLava", out bool needLava) && needLava)
            {
                AddUnique(Lang.inter[56].Value);
            }

            if (TryGetRecipeBool(recipe, NeedSnowBiomeMembers, "needSnowBiome", out bool needSnow) && needSnow)
            {
                AddUnique(Lang.inter[123].Value);
            }

            if (TryGetRecipeBool(recipe, NeedGraveyardBiomeMembers, "needGraveyardBiome", out bool needGraveyard) && needGraveyard)
            {
                AddUnique(Lang.inter[124].Value);
            }

            if (recipe.Conditions is not null)
            {
                foreach (Condition condition in recipe.Conditions)
                {
                    AddUnique(condition?.Description?.Value);
                }
            }

            return results;
        }

        private static string? ResolveRequiredTileLabel(int tileId)
        {
            if (tileId < 0)
            {
                return null;
            }

            try
            {
                int style = Recipe.GetRequiredTileStyle(tileId);
                int lookup = MapHelper.TileToLookup(tileId, style);
                string? mapObjectName = Lang.GetMapObjectName(lookup);
                if (!string.IsNullOrWhiteSpace(mapObjectName))
                {
                    return TextSanitizer.Clean(mapObjectName);
                }
            }
            catch
            {
                // Ignore lookup failures and fall back to other names.
            }

            string? tileName = TileID.Search.GetName(tileId);
            if (!string.IsNullOrWhiteSpace(tileName))
            {
                return TextSanitizer.Clean(tileName);
            }

            return $"Tile {tileId}";
        }

        private static bool TryGetRecipeBool(Recipe recipe, string[] memberNames, string debugName, out bool value)
        {
            value = false;
            if (recipe is null)
            {
                return false;
            }

            Type type = recipe.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (string memberName in memberNames)
            {
                FieldInfo? field = type.GetField(memberName, flags);
                if (field is not null)
                {
                    try
                    {
                        object? result = field.GetValue(recipe);
                        if (result is bool boolValue)
                        {
                            value = boolValue;
                            return true;
                        }
                    }
                    catch (Exception ex) when (!_loggedRecipeFlagReflectionWarning)
                    {
                        _loggedRecipeFlagReflectionWarning = true;
                        ScreenReaderMod.Instance?.Logger.Warn($"[CraftingNarrator] Failed to read recipe field {memberName}: {ex}");
                    }

                    return false;
                }

                PropertyInfo? property = type.GetProperty(memberName, flags);
                if (property is not null && property.GetIndexParameters().Length == 0 && property.PropertyType == typeof(bool))
                {
                    try
                    {
                        object? result = property.GetValue(recipe);
                        if (result is bool boolValue)
                        {
                            value = boolValue;
                            return true;
                        }
                    }
                    catch (Exception ex) when (!_loggedRecipeFlagReflectionWarning)
                    {
                        _loggedRecipeFlagReflectionWarning = true;
                        ScreenReaderMod.Instance?.Logger.Warn($"[CraftingNarrator] Failed to read recipe property {memberName}: {ex}");
                    }

                    return false;
                }
            }

            if (!_loggedRecipeFlagReflectionWarning)
            {
                _loggedRecipeFlagReflectionWarning = true;
                ScreenReaderMod.Instance?.Logger.Debug($"[CraftingNarrator] Unable to locate recipe flag members for {debugName}");
            }

            return false;
        }

        private static int CountIngredients(Recipe recipe)
        {
            IList<Item>? requiredItems = recipe.requiredItem;
            if (requiredItems is null || requiredItems.Count == 0)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < requiredItems.Count; i++)
            {
                Item ingredient = requiredItems[i];
                if (ingredient is null)
                {
                    continue;
                }

                if (ingredient.type == 0)
                {
                    break;
                }

                if (!ingredient.IsAir && ingredient.stack > 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static int FindIngredientIndex(Recipe recipe, Item candidate, IngredientIdentity identity)
        {
            IList<Item>? requiredItems = recipe.requiredItem;
            if (requiredItems is null || requiredItems.Count == 0)
            {
                return -1;
            }

            for (int i = 0; i < requiredItems.Count; i++)
            {
                Item ingredient = requiredItems[i];
                if (ingredient is null)
                {
                    continue;
                }

                if (ingredient.type == 0)
                {
                    break;
                }

                if (ReferenceEquals(ingredient, candidate))
                {
                    return i;
                }

                if (!ingredient.IsAir && IngredientIdentity.From(ingredient).Equals(identity))
                {
                    return i;
                }
            }

            return -1;
        }

        private bool TryAnnounceIngredientFocus(Recipe recipe, int recipeIndex, int ingredientCount, int currentPoint)
        {
            if (currentPoint < 0)
            {
                ResetIngredientFocus();
                return false;
            }

            if (!InventoryNarrator.TryGetItemForLinkPoint(currentPoint, out Item? focusedItem, out int context))
            {
                ResetIngredientFocus();
                return false;
            }

            if (Math.Abs(context) != Math.Abs(ItemSlot.Context.CraftingMaterial))
            {
                ResetIngredientFocus();
                return false;
            }

            if (focusedItem is null)
            {
                ResetIngredientFocus();
                return false;
            }

            IngredientIdentity identity = IngredientIdentity.From(focusedItem);
            if (identity.IsAir)
            {
                ResetIngredientFocus();
                return false;
            }

            int ingredientIndex = FindIngredientIndex(recipe, focusedItem, identity);
            if (ingredientIndex < 0)
            {
                ResetIngredientFocus();
                return false;
            }

            string? label = DescribeRequirement(recipe, recipe.requiredItem[ingredientIndex], ingredientIndex);
            if (string.IsNullOrWhiteSpace(label))
            {
                label = ComposeItemLabel(focusedItem);
            }

            string message = ingredientCount > 0
                ? $"Ingredient {ingredientIndex + 1} of {ingredientCount}, {label}"
                : $"Ingredient, {label}";

            message = GlyphTagFormatter.Normalize(message);

            if (_lastIngredientRecipeIndex == recipeIndex &&
                _lastIngredientPoint == currentPoint &&
                identity.Equals(_lastIngredientIdentity) &&
                string.Equals(message, _lastIngredientMessage, StringComparison.Ordinal))
            {
                return true;
            }

            _lastIngredientRecipeIndex = recipeIndex;
            _lastIngredientPoint = currentPoint;
            _lastIngredientIdentity = identity;
            _lastIngredientMessage = message;

            ScreenReaderService.Announce(message, force: true);
            return true;
        }

        private static string? DescribeRequirement(Recipe recipe, Item ingredient, int index)
        {
            if (recipe is null ||
                ingredient is null ||
                ingredient.type == 0 ||
                ingredient.stack <= 0 ||
                ingredient.IsAir)
            {
                return null;
            }

            string? label = ResolveRequirementLabel(recipe, ingredient, index);
            if (string.IsNullOrWhiteSpace(label))
            {
                label = $"Item {ingredient.type}";
            }

            string sanitized = GlyphTagFormatter.Normalize(label);
            int stack = Math.Max(1, ingredient.stack);
            return stack > 1 ? $"{stack} {sanitized}" : sanitized;
        }

        private static string? ResolveRequirementLabel(Recipe recipe, Item ingredient, int index)
        {
            if (recipe is null || ingredient is null)
            {
                return null;
            }

            if (TryResolveProcessGroupLabel(recipe, ingredient, out string? groupLabel))
            {
                return groupLabel;
            }

            string? anyLabel = ResolveAnyRequirementLabel(recipe, ingredient);
            if (!string.IsNullOrWhiteSpace(anyLabel))
            {
                return anyLabel;
            }

            int groupId = GetAcceptedGroupId(recipe, index);
            if (groupId >= 0)
            {
                try
                {
                    Dictionary<int, RecipeGroup>? groups = RecipeGroup.recipeGroups;
                    if (groups is not null && groups.TryGetValue(groupId, out RecipeGroup? group) && group is not null)
                    {
                        string? value = group.GetText();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return TextSanitizer.Clean(value);
                        }
                    }
                }
                catch (Exception ex) when (!_loggedRecipeGroupReflectionWarning)
                {
                    _loggedRecipeGroupReflectionWarning = true;
                    ScreenReaderMod.Instance?.Logger.Warn($"[CraftingNarrator] Failed to resolve recipe group {groupId}: {ex}");
                }
            }

            string name = TextSanitizer.Clean(ingredient.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = TextSanitizer.Clean(Lang.GetItemNameValue(ingredient.type));
            }

            return name;
        }

        private static bool TryResolveProcessGroupLabel(Recipe recipe, Item ingredient, out string? label)
        {
            label = null;
            try
            {
                if (recipe.ProcessGroupsForText(ingredient.type, out string? text) && !string.IsNullOrWhiteSpace(text))
                {
                    label = TextSanitizer.Clean(text);
                    return true;
                }
            }
            catch (Exception ex) when (!_loggedRecipeGroupReflectionWarning)
            {
                _loggedRecipeGroupReflectionWarning = true;
                ScreenReaderMod.Instance?.Logger.Warn($"[CraftingNarrator] Failed to process recipe group text: {ex}");
            }

            return false;
        }

        private static string? ResolveAnyRequirementLabel(Recipe recipe, Item ingredient)
        {
            string prefix = TextSanitizer.Clean(Lang.misc[37].Value);
            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = "Any";
            }

            static string CombineAnyLabel(string prefix, string? suffix)
            {
                string cleanedSuffix = TextSanitizer.Clean(suffix);
                if (string.IsNullOrWhiteSpace(cleanedSuffix))
                {
                    return prefix;
                }

                return $"{prefix} {cleanedSuffix}";
            }

            if (TryGetRecipeBool(recipe, AnyIronBarMembers, "anyIronBar", out bool anyIronBar) && anyIronBar && ingredient.type == ItemID.IronBar)
            {
                return CombineAnyLabel(prefix, Lang.GetItemNameValue(ItemID.IronBar));
            }

            if (TryGetRecipeBool(recipe, AnyWoodMembers, "anyWood", out bool anyWood) && anyWood && ingredient.type == ItemID.Wood)
            {
                return CombineAnyLabel(prefix, Lang.GetItemNameValue(ItemID.Wood));
            }

            if (TryGetRecipeBool(recipe, AnySandMembers, "anySand", out bool anySand) && anySand && ingredient.type == ItemID.SandBlock)
            {
                return CombineAnyLabel(prefix, Lang.GetItemNameValue(ItemID.SandBlock));
            }

            if (TryGetRecipeBool(recipe, AnyFragmentMembers, "anyFragment", out bool anyFragment) && anyFragment && ingredient.type == ItemID.FragmentSolar)
            {
                return CombineAnyLabel(prefix, Lang.misc[51].Value);
            }

            const int PressurePlateItemId = 542;
            if (TryGetRecipeBool(recipe, AnyPressurePlateMembers, "anyPressurePlate", out bool anyPressurePlate) && anyPressurePlate && ingredient.type == PressurePlateItemId)
            {
                return CombineAnyLabel(prefix, Lang.misc[38].Value);
            }

            return null;
        }

        private static int GetAcceptedGroupId(Recipe recipe, int index)
        {
            if (recipe is null || index < 0)
            {
                return -1;
            }

            if (AcceptedGroupResolver is not null)
            {
                try
                {
                    int value = AcceptedGroupResolver(recipe, index);
                    if (value >= 0)
                    {
                        return value;
                    }
                }
                catch (Exception ex) when (!_loggedRecipeGroupReflectionWarning)
                {
                    _loggedRecipeGroupReflectionWarning = true;
                    ScreenReaderMod.Instance?.Logger.Warn($"[CraftingNarrator] Failed to inspect recipe accepted groups: {ex}");
                }
            }

            IList<Item>? requiredItems = recipe.requiredItem;
            if (requiredItems is not null &&
                index >= 0 &&
                index < requiredItems.Count &&
                RecipeGroupLookup.Value.TryGetValue(requiredItems[index].type, out int fallbackGroup) &&
                fallbackGroup >= 0)
            {
                return fallbackGroup;
            }

            return -1;
        }

        private static Dictionary<int, int> DiscoverRecipeGroupLookup()
        {
            var result = new Dictionary<int, int>();

            try
            {
                Type groupType = typeof(RecipeGroup);
                BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                string[] candidateNames =
                {
                    "recipeGroupIDs",
                    "recipeGroupLookup",
                    "recipeGroupLookupTable",
                    "_recipeGroupIDs",
                    "_recipeGroupLookup"
                };

                foreach (string fieldName in candidateNames)
                {
                    FieldInfo? field = groupType.GetField(fieldName, flags);
                    if (field is null)
                    {
                        continue;
                    }

                    object? value = field.GetValue(null);
                    if (value is IDictionary dictionary)
                    {
                        foreach (DictionaryEntry entry in dictionary)
                        {
                            if (entry.Key is int key && entry.Value is int id)
                            {
                                result[key] = id;
                            }
                        }
                    }
                    else if (value is IEnumerable<KeyValuePair<int, int>> pairs)
                    {
                        foreach (KeyValuePair<int, int> pair in pairs)
                        {
                            result[pair.Key] = pair.Value;
                        }
                    }

                    if (result.Count > 0)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex) when (!_loggedRecipeGroupReflectionWarning)
            {
                _loggedRecipeGroupReflectionWarning = true;
                ScreenReaderMod.Instance?.Logger.Warn($"[CraftingNarrator] Failed to discover recipe group lookup: {ex}");
            }

            return result;
        }

        private static Func<Recipe, int, int>? CreateAcceptedGroupResolver()
        {
            try
            {
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                string[] candidateFields = { "acceptedGroup", "_acceptedGroup", "AcceptedGroup" };

                foreach (string fieldName in candidateFields)
                {
                    FieldInfo? field = typeof(Recipe).GetField(fieldName, flags);
                    if (field is null)
                    {
                        continue;
                    }

                    if (field.FieldType == typeof(int[]))
                    {
                        return (recipe, index) =>
                        {
                            if (recipe is null || index < 0)
                            {
                                return -1;
                            }

                            if (field.GetValue(recipe) is int[] groups && index < groups.Length)
                            {
                                return groups[index];
                            }

                            return -1;
                        };
                    }

                    if (typeof(IList<int>).IsAssignableFrom(field.FieldType))
                    {
                        return (recipe, index) =>
                        {
                            if (recipe is null || index < 0)
                            {
                                return -1;
                            }

                            if (field.GetValue(recipe) is IList<int> list && index < list.Count)
                            {
                                return list[index];
                            }

                            return -1;
                        };
                    }
                }
            }
            catch (Exception ex) when (!_loggedRecipeGroupReflectionWarning)
            {
                _loggedRecipeGroupReflectionWarning = true;
                ScreenReaderMod.Instance?.Logger.Warn($"[CraftingNarrator] Failed to bind accepted group resolver: {ex}");
            }

            return null;
        }
    }

    private sealed class SmartCursorNarrator
    {
        private string? _lastAnnouncement;
        private int _lastTileX = int.MinValue;
        private int _lastTileY = int.MinValue;
        private int _lastNpc = -1;
        private int _lastProj = -1;
        private int _lastInteractTileType = -1;
        private int _lastCursorTileType = -1;
        private bool _lastSmartCursorEnabled;
        private string? _pendingStatePrefix;
        private bool _suppressCursorAnnouncement;

        public void Update()
        {
            Player player = Main.LocalPlayer;
            if (player is null || !player.active)
            {
                Reset();
                return;
            }

            if (InventoryNarrator.IsInventoryUiOpen(player))
            {
                Reset();
                return;
            }

            bool hasInteract = Main.HasSmartInteractTarget;
            bool hasSmartCursor = Main.SmartCursorIsUsed || Main.SmartCursorWanted;

            if (_lastSmartCursorEnabled != hasSmartCursor)
            {
                if (hasSmartCursor)
                {
                    _pendingStatePrefix = "Smart cursor enabled";
                }
                else
                {
                    _pendingStatePrefix = null;
                    _suppressCursorAnnouncement = false;
                }

                ResetStateTracking();
                _lastSmartCursorEnabled = hasSmartCursor;

                if (!hasSmartCursor)
                {
                    AnnouncePendingStateIfAny(force: true);
                }
            }

            if (!hasInteract && !hasSmartCursor)
            {
                AnnouncePendingStateIfAny(force: true);
                Reset();
                return;
            }

            string? message = hasInteract ? DescribeSmartInteract() : DescribeSmartCursor();
            if (string.IsNullOrWhiteSpace(message))
            {
                AnnouncePendingStateIfAny();
                return;
            }

            if (!string.IsNullOrWhiteSpace(_pendingStatePrefix))
            {
                message = $"{_pendingStatePrefix}, {message}";
                _pendingStatePrefix = null;
            }

            if (string.Equals(message, _lastAnnouncement, StringComparison.Ordinal))
            {
                return;
            }

            _lastAnnouncement = message;
            ScreenReaderService.Announce(message);
        }

        private void ResetStateTracking()
        {
            _lastAnnouncement = null;
            _lastTileX = int.MinValue;
            _lastTileY = int.MinValue;
            _lastNpc = -1;
            _lastProj = -1;
            _lastInteractTileType = -1;
            _lastCursorTileType = -1;
        }

        private void Reset()
        {
            ResetStateTracking();
            _pendingStatePrefix = null;
            _suppressCursorAnnouncement = false;
        }

        private string? DescribeSmartInteract()
        {
            int npcIndex = Main.SmartInteractNPC;
            if (npcIndex >= 0 && npcIndex < Main.npc.Length)
            {
                string npcName = Main.npc[npcIndex].GivenOrTypeName;
                if (npcIndex == _lastNpc && string.Equals(npcName, _lastAnnouncement, StringComparison.Ordinal))
                {
                    return null;
                }

                _lastNpc = npcIndex;
                _lastProj = -1;
                _lastTileX = int.MinValue;
                _lastTileY = int.MinValue;
                _lastInteractTileType = -1;

                return npcName;
            }

            int projIndex = Main.SmartInteractProj;
            if (projIndex >= 0 && projIndex < Main.projectile.Length)
            {
                string projName = Lang.GetProjectileName(Main.projectile[projIndex].type).Value;
                if (string.IsNullOrWhiteSpace(projName))
                {
                    projName = $"Projectile {Main.projectile[projIndex].type}";
                }

                if (projIndex == _lastProj && string.Equals(projName, _lastAnnouncement, StringComparison.Ordinal))
                {
                    return null;
                }

                _lastProj = projIndex;
                _lastNpc = -1;
                _lastTileX = int.MinValue;
                _lastTileY = int.MinValue;
                _lastInteractTileType = -1;

                return projName;
            }

            int tileX = Main.SmartInteractX;
            int tileY = Main.SmartInteractY;
            if (tileX >= 0 && tileY >= 0)
            {
                if (!TileDescriptor.TryDescribe(tileX, tileY, out int tileType, out string? tileName))
                {
                    return null;
                }

                if (tileX == _lastTileX && tileY == _lastTileY && string.Equals(tileName, _lastAnnouncement, StringComparison.Ordinal))
                {
                    return null;
                }

                if (tileType == _lastInteractTileType)
                {
                    return null;
                }

                _lastTileX = tileX;
                _lastTileY = tileY;
                _lastNpc = -1;
                _lastProj = -1;
                _lastInteractTileType = tileType;

                if (!string.IsNullOrWhiteSpace(tileName))
                {
                    return tileName;
                }
            }

            return null;
        }

        private string? DescribeSmartCursor()
        {
            int tileX = Main.SmartCursorX;
            int tileY = Main.SmartCursorY;
            if (!TileDescriptor.TryDescribe(tileX, tileY, out int tileType, out string? tileName))
            {
                return null;
            }

            if (tileX == _lastTileX && tileY == _lastTileY && string.Equals(tileName, _lastAnnouncement, StringComparison.Ordinal))
            {
                return null;
            }

            if (tileType == _lastCursorTileType)
            {
                return null;
            }

            _lastTileX = tileX;
            _lastTileY = tileY;
            _lastNpc = -1;
            _lastProj = -1;
            _lastCursorTileType = tileType;

            if (string.IsNullOrWhiteSpace(tileName))
            {
                return null;
            }

            return tileName;
        }

        private void AnnouncePendingStateIfAny(bool force = false)
        {
            if (string.IsNullOrWhiteSpace(_pendingStatePrefix))
            {
                return;
            }

            string prefix = _pendingStatePrefix;
            _pendingStatePrefix = null;

            if (_suppressCursorAnnouncement)
            {
                CursorNarrator.SuppressNextAnnouncement();
                _suppressCursorAnnouncement = false;
            }

            if (string.Equals(prefix, _lastAnnouncement, StringComparison.Ordinal))
            {
                return;
            }

            _lastAnnouncement = prefix;
            ScreenReaderService.Announce(prefix, force: force);
        }

    }

    private sealed class CursorNarrator
    {
        private int _lastTileX = int.MinValue;
        private int _lastTileY = int.MinValue;
        private bool _lastSmartCursorActive;
        private bool _wasHoveringPlayer;
        private int _originTileX = int.MinValue;
        private int _originTileY = int.MinValue;
        private static SoundEffect? _cursorTone;
        private static readonly List<SoundEffectInstance> ActiveInstances = new();
        private static bool _suppressNextAnnouncement;

        public void Update()
        {
            Player player = Main.LocalPlayer;
            if (player is null || !player.active)
            {
                ResetAll();
                return;
            }

            bool smartCursorActive = Main.SmartCursorIsUsed || Main.SmartCursorWanted;
            bool hasSmartInteract = Main.HasSmartInteractTarget;
            bool cursorIsFree = !smartCursorActive && !hasSmartInteract;

            if (_lastSmartCursorActive && !smartCursorActive && cursorIsFree)
            {
                CenterCursorOnPlayer(player);
            }

            _lastSmartCursorActive = smartCursorActive;

            if (!cursorIsFree)
            {
                ResetCursorFeedback();
                return;
            }

            UpdateOriginFromPlayer(player);

            if (ConsumeSuppressionFlag())
            {
                _wasHoveringPlayer = IsHoveringPlayer(player);
                return;
            }

            if (PlayerInput.UsingGamepadUI && InventoryNarrator.IsInventoryUiOpen(player))
            {
                return;
            }

            Vector2 world = Main.MouseWorld;
            int tileX = (int)(world.X / 16f);
            int tileY = (int)(world.Y / 16f);

            bool wasHoveringPlayer = _wasHoveringPlayer;
            bool tileChanged = tileX != _lastTileX || tileY != _lastTileY;
            if (tileChanged)
            {
                Vector2 tileCenter = new(tileX * 16f + 8f, tileY * 16f + 8f);
                PlayCursorCue(player, tileCenter);

                _lastTileX = tileX;
                _lastTileY = tileY;
            }

            bool hoveringPlayer = IsHoveringPlayer(player);

            if (!PlayerInput.UsingGamepad)
            {
                _wasHoveringPlayer = hoveringPlayer;
                return;
            }

            if (hoveringPlayer)
            {
                if (!wasHoveringPlayer || tileChanged)
                {
                    AnnouncePlayer(player);
                }

                _wasHoveringPlayer = true;
                return;
            }

            _wasHoveringPlayer = false;

            bool shouldAnnounceTile = tileChanged || wasHoveringPlayer;
            if (!shouldAnnounceTile)
            {
                return;
            }

            TileDescriptor.TryDescribe(tileX, tileY, out _, out string? name);

            string coordinates = BuildCoordinateMessage(tileX, tileY);
            if (string.IsNullOrWhiteSpace(name))
            {
                if (!string.IsNullOrWhiteSpace(coordinates))
                {
                    ScreenReaderService.Announce(coordinates, force: true);
                }
                return;
            }

            string message = string.IsNullOrWhiteSpace(coordinates) ? name : $"{name}, {coordinates}";
            ScreenReaderService.Announce(message, force: true);
        }

        private void ResetAll()
        {
            ResetCursorFeedback();
            _lastSmartCursorActive = false;
        }

        private void ResetCursorFeedback()
        {
            ResetTileTracking();
        }

        private void ResetTileTracking()
        {
            _lastTileX = int.MinValue;
            _lastTileY = int.MinValue;
            _wasHoveringPlayer = false;
            _originTileX = int.MinValue;
            _originTileY = int.MinValue;
        }

        private static bool IsHoveringPlayer(Player player)
        {
            Vector2 world = Main.MouseWorld;
            Rectangle bounds = player.getRect();
            bounds.Inflate(4, 4);
            return bounds.Contains((int)world.X, (int)world.Y);
        }

        private void AnnouncePlayer(Player _)
        {
            ScreenReaderService.Announce("You", force: true);
        }

        public static void SuppressNextAnnouncement()
        {
            _suppressNextAnnouncement = true;
        }

        private static bool ConsumeSuppressionFlag()
        {
            if (!_suppressNextAnnouncement)
            {
                return false;
            }

            _suppressNextAnnouncement = false;
            return true;
        }

        private static void CenterCursorOnPlayer(Player player)
        {
            Vector2 screenSpace = player.Center - Main.screenPosition;
            int centeredX = (int)MathHelper.Clamp(screenSpace.X, 0f, Main.screenWidth - 1);
            int centeredY = (int)MathHelper.Clamp(screenSpace.Y, 0f, Main.screenHeight - 1);

            Main.mouseX = centeredX;
            Main.mouseY = centeredY;
            PlayerInput.MouseX = centeredX;
            PlayerInput.MouseY = centeredY;
        }

        private void UpdateOriginFromPlayer(Player player)
        {
            Vector2 chestWorld = GetPlayerChestWorld(player);
            _originTileX = (int)(chestWorld.X / 16f);
            _originTileY = (int)(chestWorld.Y / 16f);
        }

        private static Vector2 GetPlayerChestWorld(Player player)
        {
            const float chestFraction = 0.25f;
            float verticalOffset = player.height * chestFraction * player.gravDir;
            return player.Center - new Vector2(0f, verticalOffset);
        }

        private string BuildCoordinateMessage(int tileX, int tileY)
        {
            if (_originTileX == int.MinValue || _originTileY == int.MinValue)
            {
                return string.Empty;
            }

            int offsetX = tileX - _originTileX;
            int offsetY = tileY - _originTileY;

            List<string> parts = new();

            if (offsetX != 0)
            {
                string direction = offsetX > 0 ? "right" : "left";
                parts.Add($"{Math.Abs(offsetX)} {direction}");
            }

            if (offsetY != 0)
            {
                string direction = offsetY > 0 ? "down" : "up";
                parts.Add($"{Math.Abs(offsetY)} {direction}");
            }

            if (parts.Count == 0)
            {
                return "origin";
            }

            return string.Join(", ", parts);
        }

        private static void PlayCursorCue(Player player, Vector2 tileCenterWorld)
        {
            CleanupFinishedInstances();

            Vector2 offset = tileCenterWorld - player.Center;
            SoundEffect tone = EnsureCursorTone();

            float pan = MathHelper.Clamp(offset.X / 480f, -1f, 1f);
            float pitch = MathHelper.Clamp(-offset.Y / 320f, -0.6f, 0.6f);
            float volume = MathHelper.Clamp(0.35f + Math.Abs(pitch) * 0.2f, 0f, 0.7f);

            SoundEffectInstance instance = tone.CreateInstance();
            instance.IsLooped = false;
            instance.Volume = volume * Main.soundVolume;
            instance.Pitch = pitch;
            instance.Pan = pan;
            instance.Play();
            ActiveInstances.Add(instance);
        }

        public static void DisposeStaticResources()
        {
            foreach (SoundEffectInstance instance in ActiveInstances)
            {
                try
                {
                    instance.Stop();
                }
                catch
                {
                    // ignore
                }

                instance.Dispose();
            }

            ActiveInstances.Clear();

            if (_cursorTone is not null)
            {
                _cursorTone.Dispose();
                _cursorTone = null;
            }
        }

        private static SoundEffect EnsureCursorTone()
        {
            if (_cursorTone is null || _cursorTone.IsDisposed)
            {
                _cursorTone?.Dispose();
                _cursorTone = CreateCursorTone();
            }

            return _cursorTone;
        }

        private static SoundEffect CreateCursorTone()
        {
            const int sampleRate = 44100;
            const float durationSeconds = 0.09f;
            const float frequency = 880f;
            int sampleCount = Math.Max(1, (int)(sampleRate * durationSeconds));
            byte[] buffer = new byte[sampleCount * sizeof(short)];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float window = (float)(0.5 - 0.5 * Math.Cos((2 * Math.PI * i) / Math.Max(1, sampleCount - 1)));
                float sample = MathF.Sin(MathHelper.TwoPi * frequency * t) * window;
                short value = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);

                int index = i * 2;
                buffer[index] = (byte)(value & 0xFF);
                buffer[index + 1] = (byte)((value >> 8) & 0xFF);
            }

            return new SoundEffect(buffer, sampleRate, AudioChannels.Mono);
        }

        private static void CleanupFinishedInstances()
        {
            for (int i = ActiveInstances.Count - 1; i >= 0; i--)
            {
                SoundEffectInstance instance = ActiveInstances[i];
                if (instance.State == SoundState.Stopped)
                {
                    instance.Dispose();
                    ActiveInstances.RemoveAt(i);
                }
            }
        }
    }

    private static string ComposeItemLabel(Item item)
    {
        string name = TextSanitizer.Clean(item.AffixName());
        if (string.IsNullOrWhiteSpace(name))
        {
            name = TextSanitizer.Clean(item.Name);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = TextSanitizer.Clean(Lang.GetItemNameValue(item.type));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Item {item.type}";
        }

        var parts = new List<string> { name };

        if (item.stack > 1)
        {
            parts.Add($"stack {item.stack}");
        }

        if (item.favorited)
        {
            parts.Add("favorited");
        }

        return TextSanitizer.JoinWithComma(parts.ToArray());
    }

    private static class TileDescriptor
    {
        public static bool TryDescribe(int tileX, int tileY, out int tileType, out string? name)
        {
            tileType = -1;
            name = string.Empty;

            if (!WorldGen.InWorld(tileX, tileY))
            {
                return false;
            }

            Tile tile = Main.tile[tileX, tileY];
            if (!tile.HasTile)
            {
                return false;
            }

            tileType = tile.TileType;

            try
            {
                int lookup = MapHelper.TileToLookup(tileType, 0);
                name = Lang.GetMapObjectName(lookup);
            }
            catch
            {
                name = null;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = TileID.Search.GetName(tileType);
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"tile {tileType}";
            }

            return true;
        }
    }
}
