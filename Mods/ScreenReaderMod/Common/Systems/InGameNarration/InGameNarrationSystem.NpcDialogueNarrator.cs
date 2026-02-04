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
    private sealed class NpcDialogueNarrator
    {
        private const uint ButtonAnnouncementCooldownFrames = 30; // ~0.5 seconds at 60fps
        private const string SuppressionKeyButton = "npc-dialogue:button";
        private const string CooldownKeyButton = "npc-dialogue:button-cooldown";

        private int _lastNpc = -1;
        private string? _lastChat;
        private bool _lastPrimaryFocus;
        private bool _lastCloseFocus;
        private bool _lastSecondaryFocus;
        private bool _lastHappinessFocus;
        private int _lastAnnouncedButtonIndex = -1;
        private ButtonType _lastAnnouncedButtonType;

        private static string? _currentPrimaryButton;
        private static string? _currentCloseButton;
        private static string? _currentSecondaryButton;
        private static string? _currentHappinessButton;

        public void Update(NarrationServiceContext context)
        {
            Player player = context.Player;
            if (!IsLocalPlayer(player))
            {
                ResetState();
                return;
            }

            NPC? npc = TryGetActiveNpc(player.talkNPC);
            if (npc is null)
            {
                ResetState();
                return;
            }

            ScreenReaderService.AnnouncementCategory category = ResolveCategory(context);

            if (npc.whoAmI != _lastNpc)
            {
                OnNpcChanged(npc, category);
            }

            bool interruptsAllowed = ScreenReaderService.SpeechInterruptEnabled;

            HandleNpcChat(npc, category, interruptsAllowed);
            HandleTypedInput(player, category, interruptsAllowed);

            bool allowInterrupt = interruptsAllowed;

            // Build ordered list of available buttons for position announcements
            var availableButtons = BuildAvailableButtonsList();
            int totalButtons = availableButtons.Count;

            HandleButtonFocus(Main.npcChatFocus2, ref _lastPrimaryFocus, _currentPrimaryButton, allowInterrupt, category, ButtonType.Primary, availableButtons, totalButtons);
            HandleButtonFocus(Main.npcChatFocus1, ref _lastCloseFocus, _currentCloseButton, allowInterrupt, category, ButtonType.Close, availableButtons, totalButtons);
            HandleButtonFocus(Main.npcChatFocus3, ref _lastSecondaryFocus, _currentSecondaryButton, allowInterrupt, category, ButtonType.Secondary, availableButtons, totalButtons);
            HandleButtonFocus(Main.npcChatFocus4, ref _lastHappinessFocus, _currentHappinessButton, allowInterrupt, category, ButtonType.Happiness, availableButtons, totalButtons);
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

        private void OnNpcChanged(NPC npc, ScreenReaderService.AnnouncementCategory category)
        {
            ResetFocus();
            _lastNpc = npc.whoAmI;
            _lastChat = null;
            _lastAnnouncedButtonIndex = -1;
            // Clear any pending prefixes and cooldowns from previous NPC
            ScreenReaderService.ClearAllPrefixes();
            ScreenReaderService.ClearCooldown(CooldownKeyButton);
            NpcDialogueInputTracker.Reset();
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

        private void HandleButtonFocus(
            bool isFocused,
            ref bool lastState,
            string? label,
            bool allowInterrupt,
            ScreenReaderService.AnnouncementCategory category,
            ButtonType buttonType,
            List<ButtonType> availableButtons,
            int totalButtons)
        {
            if (!isFocused)
            {
                lastState = false;
                return;
            }

            if (!lastState && !string.IsNullOrWhiteSpace(label))
            {
                // Check one-shot suppression (used after typing input)
                if (ScreenReaderService.CheckAndClearSuppression(SuppressionKeyButton))
                {
                    lastState = true;
                    return;
                }

                // Debounce: skip if we just announced this same button recently
                // This prevents double announcements when clicking a button causes brief focus toggle
                if (buttonType == _lastAnnouncedButtonType && ScreenReaderService.IsOnCooldown(CooldownKeyButton))
                {
                    lastState = true;
                    return;
                }

                string trimmed = label.Trim();

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
        }

        private string? GetCurrentFocusedButtonInfo(bool updateCooldown = false)
        {
            var availableButtons = BuildAvailableButtonsList();
            int totalButtons = availableButtons.Count;

            // Check which button is currently focused and build its info
            ButtonType? focusedType = null;
            string? label = null;

            if (Main.npcChatFocus2 && !string.IsNullOrWhiteSpace(_currentPrimaryButton))
            {
                focusedType = ButtonType.Primary;
                label = _currentPrimaryButton;
            }
            else if (Main.npcChatFocus1 && !string.IsNullOrWhiteSpace(_currentCloseButton))
            {
                focusedType = ButtonType.Close;
                label = _currentCloseButton;
            }
            else if (Main.npcChatFocus3 && !string.IsNullOrWhiteSpace(_currentSecondaryButton))
            {
                focusedType = ButtonType.Secondary;
                label = _currentSecondaryButton;
            }
            else if (Main.npcChatFocus4 && !string.IsNullOrWhiteSpace(_currentHappinessButton))
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
            _lastChat = null;
            _lastAnnouncedButtonIndex = -1;
            ResetFocus();
            // Clear speech queue state related to NPC dialogue
            ScreenReaderService.ClearAllPrefixes();
            ScreenReaderService.ClearCooldown(CooldownKeyButton);
            _currentPrimaryButton = null;
            _currentCloseButton = null;
            _currentSecondaryButton = null;
            _currentHappinessButton = null;
            NpcDialogueInputTracker.Reset();
        }

        private void ResetFocus()
        {
            _lastPrimaryFocus = false;
            _lastCloseFocus = false;
            _lastSecondaryFocus = false;
            _lastHappinessFocus = false;
        }
    }
}
