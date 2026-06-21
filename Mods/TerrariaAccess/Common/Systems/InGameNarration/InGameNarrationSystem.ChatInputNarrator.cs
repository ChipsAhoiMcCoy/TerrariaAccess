#nullable enable
using System;
using Microsoft.Xna.Framework.Input;
using TerrariaAccess.Common.Services;
using Terraria;
using Terraria.GameInput;

namespace TerrariaAccess.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class ChatInputNarrator
    {
        private static readonly bool DebugEnabled =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SRM_DEBUG_CHAT_INPUT")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SRM_DEBUG_DIALOGUE_INPUT")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SRM_DEBUG_INPUT"));

        private bool _chatActive;
        private bool _lastPageUp;
        private bool _lastPageDown;
        private bool _hasHistoryCursor;
        private int _historyCursor;
        private int _openedHistoryCount;
        private string? _openedLatestMessage;
        private string? _lastDebugStateKey;

        public void Reset()
        {
            _chatActive = false;
            _lastPageUp = false;
            _lastPageDown = false;
            _hasHistoryCursor = false;
            _historyCursor = -1;
            _openedHistoryCount = 0;
            _openedLatestMessage = null;
        }

        public void Update(NarrationServiceContext context)
        {
            ScreenReaderService.AnnouncementCategory category = context.Category ?? ScreenReaderService.AnnouncementCategory.Default;

            LogStateIfChanged("Update", context.Runtime);

            if (!ShouldHandle(context.Runtime))
            {
                if (_chatActive)
                {
                    LogStateForced("chat-closed", context.Runtime);
                    ScreenReaderService.Announce(BuildChatClosedAnnouncement(), category: category, force: true);
                }

                Reset();
                return;
            }

            if (!_chatActive)
            {
                _openedHistoryCount = ChatHistoryService.Count;
                _openedLatestMessage = TryGetLatestHistoryMessage();
                LogStateForced("chat-opened", context.Runtime);
                ScreenReaderService.Announce(BuildChatOpenedAnnouncement(), category: category, force: true);
                _hasHistoryCursor = false;
                _historyCursor = ChatHistoryService.Count - 1;
            }

            HandleHistoryNavigation(category);

            _chatActive = true;
        }

        private string BuildChatOpenedAnnouncement()
        {
            string? latestMessage = TryGetLatestHistoryMessage();
            return string.IsNullOrWhiteSpace(latestMessage)
                ? "Chat opened. No chat messages yet."
                : $"Chat opened. {latestMessage}";
        }

        private string BuildChatClosedAnnouncement()
        {
            string? latestMessage = TryGetLatestHistoryMessage();
            bool latestChanged = ChatHistoryService.Count != _openedHistoryCount ||
                !string.Equals(latestMessage, _openedLatestMessage, StringComparison.Ordinal);

            if (latestChanged && !string.IsNullOrWhiteSpace(latestMessage))
            {
                return $"{latestMessage}. Chat closed.";
            }

            return "Chat closed.";
        }

        private static string? TryGetLatestHistoryMessage()
        {
            if (ChatHistoryService.Count <= 0)
            {
                return null;
            }

            return ChatHistoryService.GetMessage(ChatHistoryService.LatestIndex);
        }

        private static bool ShouldHandle(RuntimeContextSnapshot runtime)
        {
            if (runtime.InMenu)
            {
                return false;
            }

            if (Main.gameMenu || Main.blockInput || Main.editSign || Main.editChest)
            {
                return false;
            }

            if (!Main.drawingPlayerChat)
            {
                return false;
            }

            return true;
        }

        private static string DetermineGateReason(RuntimeContextSnapshot runtime)
        {
            if (runtime.InMenu) return "runtimeInMenu";
            if (Main.gameMenu) return "gameMenu";
            if (Main.blockInput) return "blockInput";
            if (Main.editSign) return "editSign";
            if (Main.editChest) return "editChest";
            if (!Main.drawingPlayerChat) return "!drawingPlayerChat";
            return "allowed";
        }

        private void LogStateIfChanged(string context, RuntimeContextSnapshot runtime)
        {
            if (!DebugEnabled) return;

            string gate = DetermineGateReason(runtime);
            string chatText = Main.chatText ?? string.Empty;
            int chatLen = chatText.Length;
            string stateKey = string.Join("|",
                gate,
                Main.drawingPlayerChat ? 1 : 0,
                _chatActive ? 1 : 0,
                PlayerInput.CurrentInputMode,
                PlayerInput.WritingText ? 1 : 0,
                chatLen);

            if (string.Equals(stateKey, _lastDebugStateKey, StringComparison.Ordinal)) return;

            _lastDebugStateKey = stateKey;
            EmitDebug(context, runtime, gate, chatText);
        }

        private void LogStateForced(string context, RuntimeContextSnapshot runtime)
        {
            if (!DebugEnabled) return;
            string gate = DetermineGateReason(runtime);
            string chatText = Main.chatText ?? string.Empty;
            EmitDebug(context, runtime, gate, chatText);
        }

        private void EmitDebug(string context, RuntimeContextSnapshot runtime, string gate, string chatText)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Info(
                $"[ChatInputDebug] {context}: " +
                $"gate={gate} " +
                $"drawingPlayerChat={Main.drawingPlayerChat} " +
                $"chatActive={_chatActive} " +
                $"inputMode={PlayerInput.CurrentInputMode} " +
                $"writingText={PlayerInput.WritingText} " +
                $"blockInput={Main.blockInput} " +
                $"gameMenu={Main.gameMenu} " +
                $"editSign={Main.editSign} " +
                $"editChest={Main.editChest} " +
                $"runtimeInMenu={runtime.InMenu} " +
                $"chatTextLen={chatText.Length} " +
                $"frame={Main.GameUpdateCount}");
        }

        private void HandleHistoryNavigation(ScreenReaderService.AnnouncementCategory category)
        {
            bool pageUpHeld = Main.keyState.IsKeyDown(Keys.PageUp);
            bool pageDownHeld = Main.keyState.IsKeyDown(Keys.PageDown);

            bool pageUpPressed = pageUpHeld && !_lastPageUp;
            bool pageDownPressed = pageDownHeld && !_lastPageDown;

            _lastPageUp = pageUpHeld;
            _lastPageDown = pageDownHeld;

            if (!pageUpPressed && !pageDownPressed)
            {
                return;
            }

            int count = ChatHistoryService.Count;
            if (count == 0)
            {
                ScreenReaderService.Announce("No chat messages yet.", category: category, force: true);
                return;
            }

            if (!_hasHistoryCursor)
            {
                _hasHistoryCursor = true;
                _historyCursor = Math.Max(0, count - 1);
                AnnounceHistoryEntry(category, count, _historyCursor);
                return;
            }

            if (pageUpPressed)
            {
                if (_historyCursor <= 0)
                {
                    ScreenReaderService.Announce("No earlier chat messages.", category: category, force: true);
                    return;
                }

                _historyCursor--;
                AnnounceHistoryEntry(category, count, _historyCursor);
                return;
            }

            if (pageDownPressed)
            {
                if (_historyCursor >= count - 1)
                {
                    ScreenReaderService.Announce("Already at the most recent chat message.", category: category, force: true);
                    return;
                }

                _historyCursor++;
                AnnounceHistoryEntry(category, count, _historyCursor);
            }
        }

        private static void AnnounceHistoryEntry(ScreenReaderService.AnnouncementCategory category, int count, int index)
        {
            string? message = ChatHistoryService.GetMessage(index);
            if (string.IsNullOrWhiteSpace(message))
            {
                ScreenReaderService.Announce("No chat message available.", category: category);
                return;
            }

            ScreenReaderService.Announce(
                $"{message} ({index + 1} of {count})",
                category: category);
        }
    }
}
