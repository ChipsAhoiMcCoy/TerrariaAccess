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
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.MenuNarration;
using TerrariaAccess.Common.Utilities;
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

namespace TerrariaAccess.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class NpcDialogueNarrator
    {
        private const uint ButtonAnnouncementCooldownFrames = 30; // ~0.5 seconds at 60fps
        private const uint ButtonFocusLossGraceFrames = 4;
        private const string SuppressionKeyButton = "npc-dialogue:button";
        private const string CooldownKeyButton = "npc-dialogue:button-cooldown";

        private int _lastNpc = -1;
        private int _lastSign = -1;
        private string? _lastChat;
        private bool _lastPrimaryFocus;
        private bool _lastCloseFocus;
        private bool _lastSecondaryFocus;
        private bool _lastHappinessFocus;
        private uint _lastPrimaryFocusedFrame;
        private uint _lastCloseFocusedFrame;
        private uint _lastSecondaryFocusedFrame;
        private uint _lastHappinessFocusedFrame;
        private string? _lastPrimaryLabel;
        private string? _lastCloseLabel;
        private string? _lastSecondaryLabel;
        private string? _lastHappinessLabel;
        private int _lastAnnouncedButtonIndex = -1;
        private ButtonType _lastAnnouncedButtonType;
        private bool _wasSignEditing;
        private bool _lastSignTextEntryActive;
        private bool _lastSignButtonNavigationActive;

        private static string? _currentPrimaryButton;
        private static string? _currentCloseButton;
        private static string? _currentSecondaryButton;
        private static string? _currentHappinessButton;
        private static bool _drawPrimaryFocus;
        private static bool _drawCloseFocus;
        private static bool _drawSecondaryFocus;
        private static bool _drawHappinessFocus;

        public void Update(NarrationServiceContext context)
        {
            Player player = context.Player;
            if (!IsLocalPlayer(player))
            {
                ResetState();
                return;
            }

            NPC? npc = TryGetActiveNpc(player.talkNPC);
            int signIndex = npc is null ? TryGetActiveSignIndex(player) : -1;
            if (npc is null && signIndex < 0)
            {
                ResetState();
                return;
            }

            ScreenReaderService.AnnouncementCategory category = ResolveCategory(context);

            if (npc is not null && (npc.whoAmI != _lastNpc || _lastSign >= 0))
            {
                OnNpcChanged(npc, category);
            }
            else if (npc is null && (signIndex != _lastSign || _lastNpc >= 0))
            {
                OnSignChanged(signIndex);
            }

            bool interruptsAllowed = ScreenReaderService.SpeechInterruptEnabled;

            if (npc is not null)
            {
                HandleNpcChat(npc, category, interruptsAllowed);
                HandleTypedInput(player, category, interruptsAllowed);
            }
            else
            {
                HandleSignModeTransition();
                HandleSignChat(signIndex, category, interruptsAllowed);
                HandleSignTypedInput(player, category, interruptsAllowed);
            }

            bool allowInterrupt = interruptsAllowed;

            // Build ordered list of available buttons for position announcements
            var availableButtons = BuildAvailableButtonsList();
            int totalButtons = availableButtons.Count;

            HandleButtonFocus(IsButtonFocused(ButtonType.Primary), ref _lastPrimaryFocus, ref _lastPrimaryFocusedFrame, ref _lastPrimaryLabel, _currentPrimaryButton, allowInterrupt, category, ButtonType.Primary, availableButtons, totalButtons);
            HandleButtonFocus(IsButtonFocused(ButtonType.Close), ref _lastCloseFocus, ref _lastCloseFocusedFrame, ref _lastCloseLabel, _currentCloseButton, allowInterrupt, category, ButtonType.Close, availableButtons, totalButtons);
            HandleButtonFocus(IsButtonFocused(ButtonType.Secondary), ref _lastSecondaryFocus, ref _lastSecondaryFocusedFrame, ref _lastSecondaryLabel, _currentSecondaryButton, allowInterrupt, category, ButtonType.Secondary, availableButtons, totalButtons);
            HandleButtonFocus(IsButtonFocused(ButtonType.Happiness), ref _lastHappinessFocus, ref _lastHappinessFocusedFrame, ref _lastHappinessLabel, _currentHappinessButton, allowInterrupt, category, ButtonType.Happiness, availableButtons, totalButtons);
        }

        private enum ButtonType
        {
            Primary,
            Close,
            Secondary,
            Happiness
        }

        private static List<ButtonType> BuildAvailableButtonsList()
        {
            var buttons = new List<ButtonType>(4);

            // Order matches gamepad navigation: Primary (2500) -> Close (2501) -> Secondary (2502) -> Happiness (2503)
            if (!string.IsNullOrWhiteSpace(_currentPrimaryButton))
                buttons.Add(ButtonType.Primary);
            if (!string.IsNullOrWhiteSpace(_currentCloseButton))
                buttons.Add(ButtonType.Close);
            if (!string.IsNullOrWhiteSpace(_currentSecondaryButton))
                buttons.Add(ButtonType.Secondary);
            if (!string.IsNullOrWhiteSpace(_currentHappinessButton))
                buttons.Add(ButtonType.Happiness);

            return buttons;
        }

        private static bool IsLocalPlayer(Player player)
        {
            return player is not null &&
                   player.active &&
                   player.whoAmI == Main.myPlayer &&
                   Main.netMode != NetmodeID.Server;
        }

        private static NPC? TryGetActiveNpc(int npcIndex)
        {
            if (npcIndex < 0 || npcIndex >= Main.npc.Length)
            {
                return null;
            }

            NPC npc = Main.npc[npcIndex];
            return npc.active ? npc : null;
        }

        private static int TryGetActiveSignIndex(Player player)
        {
            if (player is null || player.whoAmI != Main.myPlayer)
            {
                return -1;
            }

            int signIndex = player.sign;
            if (signIndex < 0 || signIndex >= Main.sign.Length)
            {
                return -1;
            }

            return signIndex;
        }

        private void OnNpcChanged(NPC npc, ScreenReaderService.AnnouncementCategory category)
        {
            ResetFocus();
            _lastNpc = npc.whoAmI;
            _lastSign = -1;
            _lastChat = null;
            _lastAnnouncedButtonIndex = -1;
            // Clear any pending prefixes and cooldowns from previous NPC
            ScreenReaderService.ClearAllPrefixes();
            ScreenReaderService.ClearCooldown(CooldownKeyButton);
            ScreenReaderService.CheckAndClearSuppression(SuppressionKeyButton);
            ClearButtonLabels();
            NpcDialogueInputTracker.Reset();
            _lastSignTextEntryActive = false;
            _lastSignButtonNavigationActive = false;
        }

        private void OnSignChanged(int signIndex)
        {
            ResetFocus();
            _lastNpc = -1;
            _lastSign = signIndex;
            _lastChat = null;
            _lastAnnouncedButtonIndex = -1;
            ScreenReaderService.ClearAllPrefixes();
            ScreenReaderService.ClearCooldown(CooldownKeyButton);
            ScreenReaderService.CheckAndClearSuppression(SuppressionKeyButton);
            ClearButtonLabels();
            NpcDialogueInputTracker.Reset();
            _lastSignTextEntryActive = false;
            _lastSignButtonNavigationActive = false;
        }

        private void HandleNpcChat(NPC npc, ScreenReaderService.AnnouncementCategory category, bool interruptsAllowed)
        {
            string chat = Main.npcChatText ?? string.Empty;
            string normalizedText = NormalizeChat(chat);
            if (!string.IsNullOrWhiteSpace(normalizedText) &&
                !string.Equals(normalizedText, _lastChat, StringComparison.Ordinal))
            {
                string prefix = npc.GivenOrTypeName;
                string npcChatMessage = string.IsNullOrWhiteSpace(prefix)
                    ? normalizedText
                    : $"{prefix} says: {normalizedText}";

                // If this is the first chat for this NPC (no button announced yet),
                // enqueue it to bundle with the first button announcement
                if (_lastAnnouncedButtonIndex < 0)
                {
                    ScreenReaderService.EnqueuePrefix(npcChatMessage);
                }
                else
                {
                    // NPC changed what they're saying during the conversation (e.g., after clicking a button)
                    // Bundle with current button position so player knows where they are
                    // Pass updateCooldown: true to prevent HandleButtonFocus from immediately re-announcing the button
                    string? currentButtonInfo = GetCurrentFocusedButtonInfo(updateCooldown: true);
                    string announcement = string.IsNullOrWhiteSpace(currentButtonInfo)
                        ? npcChatMessage
                        : $"{npcChatMessage}. {currentButtonInfo}";

                    NarrationInstrumentationContext.SetPendingKey("npc-dialogue:text");
                    ScreenReaderService.Announce(announcement, category: category, requestInterrupt: interruptsAllowed);
                }

                _lastChat = normalizedText;
            }
            else if (string.IsNullOrWhiteSpace(normalizedText))
            {
                _lastChat = null;
                ScreenReaderService.ClearAllPrefixes();
            }
        }

        private void HandleSignChat(int signIndex, ScreenReaderService.AnnouncementCategory category, bool interruptsAllowed)
        {
            string normalizedText = NormalizeChat(Main.npcChatText ?? string.Empty);
            if (SignInputModeSystem.IsTextEntryActive)
            {
                _lastChat = normalizedText;
                return;
            }

            if (string.IsNullOrWhiteSpace(normalizedText))
            {
                _lastChat = null;
                return;
            }

            if (string.Equals(normalizedText, _lastChat, StringComparison.Ordinal))
            {
                return;
            }

            string signMessage = BuildSignAnnouncement(signIndex, normalizedText);
            if (_lastAnnouncedButtonIndex < 0)
            {
                string? currentButtonInfo = GetCurrentFocusedButtonInfo(updateCooldown: true);
                if (string.IsNullOrWhiteSpace(currentButtonInfo))
                {
                    ScreenReaderService.EnqueuePrefix(signMessage);
                }
                else
                {
                    NarrationInstrumentationContext.SetPendingKey("sign:text");
                    ScreenReaderService.Announce(
                        $"{signMessage}. {currentButtonInfo}",
                        category: category,
                        requestInterrupt: interruptsAllowed);
                }
            }
            else
            {
                string? currentButtonInfo = GetCurrentFocusedButtonInfo(updateCooldown: true);
                string announcement = string.IsNullOrWhiteSpace(currentButtonInfo)
                    ? signMessage
                    : $"{signMessage}. {currentButtonInfo}";

                NarrationInstrumentationContext.SetPendingKey("sign:text");
                ScreenReaderService.Announce(announcement, category: category, requestInterrupt: interruptsAllowed);
            }

            _lastChat = normalizedText;
        }

        private void HandleSignModeTransition()
        {
            bool textEntryActive = SignInputModeSystem.IsTextEntryActive;
            bool buttonNavigationActive = SignInputModeSystem.IsButtonNavigationActive;

            if (textEntryActive == _lastSignTextEntryActive &&
                buttonNavigationActive == _lastSignButtonNavigationActive)
            {
                return;
            }

            ResetFocus();
            _lastAnnouncedButtonIndex = -1;
            ScreenReaderService.ClearCooldown(CooldownKeyButton);

            _lastSignTextEntryActive = textEntryActive;
            _lastSignButtonNavigationActive = buttonNavigationActive;
        }

        private void HandleSignTypedInput(Player player, ScreenReaderService.AnnouncementCategory category, bool interruptsAllowed)
        {
            bool inputActive = IsTypingToSign(player);
            if (!inputActive)
            {
                _wasSignEditing = false;
                NpcDialogueInputTracker.RecordTypedInput(null, active: false);
                return;
            }

            if (!_wasSignEditing)
            {
                _wasSignEditing = true;
                NpcDialogueInputTracker.PrimeTypedInput(Main.npcChatText);
                return;
            }

            NpcDialogueInputTracker.RecordTypedInput(Main.npcChatText, active: true);
            if (!NpcDialogueInputTracker.TryDequeueTypedInput(out string typedText))
            {
                return;
            }

            NarrationInstrumentationContext.SetPendingKey("sign:typed");
            ScreenReaderService.Announce(
                $"Sign text: {typedText}",
                category: category,
                requestInterrupt: interruptsAllowed);

            ScreenReaderService.SuppressNext(SuppressionKeyButton);
        }

        private void HandleTypedInput(Player player, ScreenReaderService.AnnouncementCategory category, bool interruptsAllowed)
        {
            bool inputActive = IsTypingToNpc(player);
            NpcDialogueInputTracker.RecordTypedInput(Main.chatText, inputActive);

            if (!inputActive || !NpcDialogueInputTracker.TryDequeueTypedInput(out string typedText))
            {
                return;
            }

            NarrationInstrumentationContext.SetPendingKey("npc-dialogue:typed");
            ScreenReaderService.Announce(
                $"You typed: {typedText}",
                category: category,
                requestInterrupt: interruptsAllowed);

            // Suppress the next button announcement to avoid announcing the button after typing
            ScreenReaderService.SuppressNext(SuppressionKeyButton);
        }

        private static bool IsTypingToNpc(Player player)
        {
            if (player is null || player.whoAmI != Main.myPlayer)
            {
                return false;
            }

            if (!Main.drawingPlayerChat || Main.gameMenu || Main.blockInput || Main.editSign || Main.editChest)
            {
                return false;
            }

            return player.talkNPC >= 0;
        }

        private static bool IsTypingToSign(Player player)
        {
            if (player is null || player.whoAmI != Main.myPlayer)
            {
                return false;
            }

            if (Main.gameMenu || Main.blockInput || Main.editChest)
            {
                return false;
            }

            return SignInputModeSystem.IsTextEntryActive && player.sign >= 0;
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

        public static void UpdateButtonState(
            string? primary,
            string? close,
            string? secondary,
            string? happiness,
            bool primaryFocused,
            bool closeFocused,
            bool secondaryFocused,
            bool happinessFocused)
        {
            _currentPrimaryButton = NormalizeLabel(primary);
            _currentCloseButton = NormalizeLabel(close);
            _currentSecondaryButton = NormalizeLabel(secondary);
            _currentHappinessButton = NormalizeLabel(happiness);
            _drawPrimaryFocus = primaryFocused;
            _drawCloseFocus = closeFocused;
            _drawSecondaryFocus = secondaryFocused;
            _drawHappinessFocus = happinessFocused;
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

        private static string BuildSignAnnouncement(int signIndex, string normalizedText)
        {
            string source = GetSignSourceLabel(signIndex);
            return $"{source} reads: {normalizedText}";
        }

        private static string GetSignSourceLabel(int signIndex)
        {
            if (signIndex < 0 || signIndex >= Main.sign.Length)
            {
                return "Sign";
            }

            Sign? activeSign = Main.sign[signIndex];
            if (activeSign is null || !WorldGen.InWorld(activeSign.x, activeSign.y, 1))
            {
                return "Sign";
            }

            Tile tile = Main.tile[activeSign.x, activeSign.y];
            if (!tile.HasTile)
            {
                return "Sign";
            }

            return tile.TileType switch
            {
                TileID.Tombstones => "Grave marker",
                TileID.AnnouncementBox => "Announcement box",
                _ => "Sign",
            };
        }

        private static void ClearButtonLabels()
        {
            _currentPrimaryButton = null;
            _currentCloseButton = null;
            _currentSecondaryButton = null;
            _currentHappinessButton = null;
            _drawPrimaryFocus = false;
            _drawCloseFocus = false;
            _drawSecondaryFocus = false;
            _drawHappinessFocus = false;
        }

        private static bool IsButtonFocused(ButtonType buttonType)
        {
            if (SignInputModeSystem.IsButtonNavigationActive)
            {
                return buttonType switch
                {
                    ButtonType.Primary => SignInputModeSystem.IsSaveButtonSelected,
                    ButtonType.Close => SignInputModeSystem.IsCloseButtonSelected,
                    ButtonType.Secondary => false,
                    ButtonType.Happiness => false,
                    _ => false,
                };
            }

            return buttonType switch
            {
                ButtonType.Primary => _drawPrimaryFocus,
                ButtonType.Close => _drawCloseFocus,
                ButtonType.Secondary => _drawSecondaryFocus,
                ButtonType.Happiness => _drawHappinessFocus,
                _ => false,
            };
        }

        private void HandleButtonFocus(
            bool isFocused,
            ref bool lastState,
            ref uint lastFocusedFrame,
            ref string? lastLabel,
            string? label,
            bool allowInterrupt,
            ScreenReaderService.AnnouncementCategory category,
            ButtonType buttonType,
            List<ButtonType> availableButtons,
            int totalButtons)
        {
            uint currentFrame = Main.GameUpdateCount;
            if (!isFocused)
            {
                if (lastState && IsWithinFocusLossGrace(currentFrame, lastFocusedFrame))
                {
                    return;
                }

                lastState = false;
                return;
            }

            lastFocusedFrame = currentFrame;
            string? trimmedLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
            bool labelChangedWhileFocused = lastState &&
                !string.Equals(lastLabel, trimmedLabel, StringComparison.Ordinal);

            if ((!lastState || labelChangedWhileFocused) && !string.IsNullOrWhiteSpace(trimmedLabel))
            {
                // Check one-shot suppression (used after typing input)
                if (!labelChangedWhileFocused && ScreenReaderService.CheckAndClearSuppression(SuppressionKeyButton))
                {
                    lastState = true;
                    lastLabel = trimmedLabel;
                    return;
                }

                // Debounce: skip if we just announced this same button recently
                // This prevents double announcements when clicking a button causes brief focus toggle
                if (!labelChangedWhileFocused &&
                    buttonType == _lastAnnouncedButtonType &&
                    ScreenReaderService.IsOnCooldown(CooldownKeyButton))
                {
                    lastState = true;
                    lastLabel = trimmedLabel;
                    return;
                }

                string trimmed = trimmedLabel;

                // Build button announcement with "X of Y" position
                int buttonIndex = availableButtons.IndexOf(buttonType);
                int position = buttonIndex >= 0 ? buttonIndex + 1 : 1;
                _lastAnnouncedButtonIndex = buttonIndex;
                _lastAnnouncedButtonType = buttonType;
                ScreenReaderService.SetCooldown(CooldownKeyButton, ButtonAnnouncementCooldownFrames);

                string buttonLabel = trimmed;
                if (!trimmed.Contains("button", StringComparison.OrdinalIgnoreCase))
                {
                    buttonLabel = $"{trimmed} button";
                }

                // Add position info: "Shop button, 1 of 3"
                string announcement;
                if (totalButtons > 1)
                {
                    announcement = $"{buttonLabel}, {position} of {totalButtons}";
                }
                else
                {
                    announcement = buttonLabel;
                }

                // The NPC chat prefix was enqueued via EnqueuePrefix and will be
                // automatically prepended by the speech controller

                NarrationInstrumentationContext.SetPendingKey($"npc-dialogue:choice:{trimmed}");
                ScreenReaderService.Announce(announcement, category: category, requestInterrupt: allowInterrupt);
            }

            lastState = true;
            lastLabel = trimmedLabel;
        }

        private static bool IsWithinFocusLossGrace(uint currentFrame, uint lastFocusedFrame)
        {
            return lastFocusedFrame != 0 &&
                   currentFrame >= lastFocusedFrame &&
                   currentFrame - lastFocusedFrame <= ButtonFocusLossGraceFrames;
        }

        private string? GetCurrentFocusedButtonInfo(bool updateCooldown = false)
        {
            var availableButtons = BuildAvailableButtonsList();
            int totalButtons = availableButtons.Count;

            // Check which button is currently focused and build its info
            ButtonType? focusedType = null;
            string? label = null;

            if (IsButtonFocused(ButtonType.Primary) && !string.IsNullOrWhiteSpace(_currentPrimaryButton))
            {
                focusedType = ButtonType.Primary;
                label = _currentPrimaryButton;
            }
            else if (IsButtonFocused(ButtonType.Close) && !string.IsNullOrWhiteSpace(_currentCloseButton))
            {
                focusedType = ButtonType.Close;
                label = _currentCloseButton;
            }
            else if (IsButtonFocused(ButtonType.Secondary) && !string.IsNullOrWhiteSpace(_currentSecondaryButton))
            {
                focusedType = ButtonType.Secondary;
                label = _currentSecondaryButton;
            }
            else if (IsButtonFocused(ButtonType.Happiness) && !string.IsNullOrWhiteSpace(_currentHappinessButton))
            {
                focusedType = ButtonType.Happiness;
                label = _currentHappinessButton;
            }

            if (!focusedType.HasValue || string.IsNullOrWhiteSpace(label))
            {
                return null;
            }

            // Update cooldown tracking if requested, to prevent duplicate button announcements
            // when HandleButtonFocus runs immediately after
            if (updateCooldown)
            {
                _lastAnnouncedButtonIndex = availableButtons.IndexOf(focusedType.Value);
                _lastAnnouncedButtonType = focusedType.Value;
                ScreenReaderService.SetCooldown(CooldownKeyButton, ButtonAnnouncementCooldownFrames);
            }

            string trimmed = label.Trim();
            string buttonLabel = trimmed.Contains("button", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : $"{trimmed} button";

            if (totalButtons > 1)
            {
                int buttonIndex = availableButtons.IndexOf(focusedType.Value);
                int position = buttonIndex >= 0 ? buttonIndex + 1 : 1;
                return $"{buttonLabel}, {position} of {totalButtons}";
            }

            return buttonLabel;
        }

        private static ScreenReaderService.AnnouncementCategory ResolveCategory(NarrationServiceContext context)
        {
            return context.Category ?? ScreenReaderService.AnnouncementCategory.Default;
        }

        private void ResetState()
        {
            _lastNpc = -1;
            _lastSign = -1;
            _lastChat = null;
            _lastAnnouncedButtonIndex = -1;
            ResetFocus();
            _wasSignEditing = false;
            _lastSignTextEntryActive = false;
            _lastSignButtonNavigationActive = false;
            // Clear speech queue state related to NPC dialogue
            ScreenReaderService.ClearAllPrefixes();
            ScreenReaderService.ClearCooldown(CooldownKeyButton);
            ScreenReaderService.CheckAndClearSuppression(SuppressionKeyButton);
            ClearButtonLabels();
            NpcDialogueInputTracker.Reset();
        }

        private void ResetFocus()
        {
            _lastPrimaryFocus = false;
            _lastCloseFocus = false;
            _lastSecondaryFocus = false;
            _lastHappinessFocus = false;
            _lastPrimaryFocusedFrame = 0;
            _lastCloseFocusedFrame = 0;
            _lastSecondaryFocusedFrame = 0;
            _lastHappinessFocusedFrame = 0;
            _lastPrimaryLabel = null;
            _lastCloseLabel = null;
            _lastSecondaryLabel = null;
            _lastHappinessLabel = null;
        }
    }
}
