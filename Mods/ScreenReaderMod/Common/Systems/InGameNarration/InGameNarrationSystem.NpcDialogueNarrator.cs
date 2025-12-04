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

            HandleNpcChat(npc, category);
            HandleTypedInput(player, category);

            bool allowInterrupt = NpcDialogueInputTracker.IsNavigationPressed;

            HandleButtonFocus(Main.npcChatFocus2, ref _lastPrimaryFocus, _currentPrimaryButton, allowInterrupt, category);
            HandleButtonFocus(Main.npcChatFocus1, ref _lastCloseFocus, _currentCloseButton, allowInterrupt, category);
            HandleButtonFocus(Main.npcChatFocus3, ref _lastSecondaryFocus, _currentSecondaryButton, allowInterrupt, category);
            HandleButtonFocus(Main.npcChatFocus4, ref _lastHappinessFocus, _currentHappinessButton, allowInterrupt, category);
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
            string npcName = npc.GivenOrTypeName;
            if (!string.IsNullOrWhiteSpace(npcName))
            {
                NarrationInstrumentationContext.SetPendingKey($"npc-dialogue:npc:{npcName}");
                ScreenReaderService.Announce($"Talking to {npcName}", force: true, category: category);
            }

            _lastNpc = npc.whoAmI;
            _lastChat = null;
            _suppressNextButtonAnnouncement = true;
            NpcDialogueInputTracker.Reset();
        }

        private void HandleNpcChat(NPC npc, ScreenReaderService.AnnouncementCategory category)
        {
            string chat = Main.npcChatText ?? string.Empty;
            string normalizedText = NormalizeChat(chat);
            if (!string.IsNullOrWhiteSpace(normalizedText) &&
                !string.Equals(normalizedText, _lastChat, StringComparison.Ordinal))
            {
                string prefix = npc.GivenOrTypeName;
                string announcement = string.IsNullOrWhiteSpace(prefix)
                    ? normalizedText
                    : $"{prefix} says: {normalizedText}";

                NarrationInstrumentationContext.SetPendingKey("npc-dialogue:text");
                ScreenReaderService.Announce(announcement, category: category);

                _lastChat = normalizedText;
                _suppressNextButtonAnnouncement = true;
            }
            else if (string.IsNullOrWhiteSpace(normalizedText))
            {
                _lastChat = null;
                _suppressNextButtonAnnouncement = false;
            }
        }

        private void HandleTypedInput(Player player, ScreenReaderService.AnnouncementCategory category)
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
                requestInterrupt: true);

            _suppressNextButtonAnnouncement = true;
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
            ScreenReaderService.AnnouncementCategory category)
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

                NarrationInstrumentationContext.SetPendingKey($"npc-dialogue:choice:{trimmed}");
                ScreenReaderService.Announce(announcement, category: category, requestInterrupt: allowInterrupt);
            }

            lastState = true;
        }

        private static ScreenReaderService.AnnouncementCategory ResolveCategory(NarrationServiceContext context)
        {
            return context.Category ?? ScreenReaderService.AnnouncementCategory.Default;
        }

        private void ResetState()
        {
            _lastNpc = -1;
            _lastChat = null;
            ResetFocus();
            _suppressNextButtonAnnouncement = false;
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
