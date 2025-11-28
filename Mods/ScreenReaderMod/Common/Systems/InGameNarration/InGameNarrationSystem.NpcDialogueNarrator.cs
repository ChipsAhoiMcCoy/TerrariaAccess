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

            bool allowInterrupt = NpcDialogueInputTracker.IsNavigationPressed;

            HandleButtonFocus(Main.npcChatFocus2, ref _lastPrimaryFocus, _currentPrimaryButton, allowInterrupt);
            HandleButtonFocus(Main.npcChatFocus1, ref _lastCloseFocus, _currentCloseButton, allowInterrupt);
            HandleButtonFocus(Main.npcChatFocus3, ref _lastSecondaryFocus, _currentSecondaryButton, allowInterrupt);
            HandleButtonFocus(Main.npcChatFocus4, ref _lastHappinessFocus, _currentHappinessButton, allowInterrupt);
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

        private void HandleButtonFocus(bool isFocused, ref bool lastState, string? label, bool allowInterrupt)
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
}
