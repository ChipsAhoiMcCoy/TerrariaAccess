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
using AnnouncementCategory = ScreenReaderMod.Common.Services.ScreenReaderService.AnnouncementCategory;
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

            if (ShouldSuppressForMenus(player))
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

            AnnouncementCategory category = AnnouncementCategory.Default;
            string? message = hasInteract ? DescribeSmartInteract(out category) : DescribeSmartCursor(out category);
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
            ScreenReaderService.Announce(message, category: category);
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

        private static bool ShouldSuppressForMenus(Player player)
        {
            if (InventoryNarrator.IsInventoryUiOpen(player))
            {
                return true;
            }

            if (IsNpcDialogueActive(player))
            {
                return true;
            }

            if (IsOtherIngameUiActive())
            {
                return true;
            }

            return false;
        }

        private static bool IsNpcDialogueActive(Player player)
        {
            return player.talkNPC >= 0 || !string.IsNullOrWhiteSpace(Main.npcChatText);
        }

        private static bool IsOtherIngameUiActive()
        {
            if (Main.editSign || Main.editChest || Main.drawingPlayerChat || Main.inFancyUI)
            {
                return true;
            }

            UIState? state = Main.InGameUI?.CurrentState;
            return state is not null;
        }

        private string? DescribeSmartInteract(out AnnouncementCategory category)
        {
            category = AnnouncementCategory.Default;

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
                    category = TileDescriptor.GetAnnouncementCategory(tileType);
                    return tileName;
                }
            }

            return null;
        }

        private string? DescribeSmartCursor(out AnnouncementCategory category)
        {
            category = AnnouncementCategory.Default;

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

            category = TileDescriptor.GetAnnouncementCategory(tileType);
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
}
