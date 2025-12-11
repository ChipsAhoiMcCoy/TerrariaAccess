#nullable enable
using System;
using Microsoft.Xna.Framework.Input;
using ScreenReaderMod.Common.Services;
using Terraria;
using Terraria.GameInput;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class ChatInputNarrator
    {
        private bool _chatActive;
        private bool _lastPageUp;
        private bool _lastPageDown;
        private bool _hasHistoryCursor;
        private int _historyCursor;

        public void Reset()
        {
            _chatActive = false;
            _lastPageUp = false;
            _lastPageDown = false;
            _hasHistoryCursor = false;
            _historyCursor = -1;
        }

        public void Update(NarrationServiceContext context)
        {
            if (!ShouldHandle(context.Runtime))
            {
                Reset();
                return;
            }

            ScreenReaderService.AnnouncementCategory category = context.Category ?? ScreenReaderService.AnnouncementCategory.Default;

            if (!_chatActive)
            {
                _hasHistoryCursor = false;
                _historyCursor = ChatHistoryService.Count - 1;
            }

            HandleHistoryNavigation(category);

            _chatActive = true;
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
