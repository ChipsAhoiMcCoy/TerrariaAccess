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
        private readonly CursorDescriptorService _descriptorService;
        private string? _lastAnnouncement;
        private int _lastTileX = int.MinValue;
        private int _lastTileY = int.MinValue;
        private int _lastNpc = -1;
        private int _lastProj = -1;
        private int _lastInteractTileType = -1;
        private int _lastCursorTileType = -1;
        private int _lastCursorAnnouncementKey = int.MinValue;
        private bool _lastSmartCursorEnabled;
        private string? _pendingStatePrefix;
        private bool _suppressCursorAnnouncement;
        private bool _lastIsToggleOn;

        // Static pending prefix for CursorNarrator to bundle with its next announcement
        private static string? _pendingCursorPrefix;

        internal static bool TryDequeuePendingPrefix(out string prefix)
        {
            if (_pendingCursorPrefix is null)
            {
                prefix = string.Empty;
                return false;
            }

            prefix = _pendingCursorPrefix;
            _pendingCursorPrefix = null;
            return true;
        }

        internal static void ClearPendingPrefix()
        {
            _pendingCursorPrefix = null;
        }

        public SmartCursorNarrator(CursorDescriptorService descriptorService)
        {
            _descriptorService = descriptorService;
        }

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
                    _pendingStatePrefix = LocalizationHelper.GetTextOrFallback(
                        "Mods.ScreenReaderMod.SmartCursor.Enabled",
                        "Smart cursor");
                }
                else
                {
                    // Set pending prefix for CursorNarrator to bundle with its first tile announcement
                    _pendingCursorPrefix = LocalizationHelper.GetTextOrFallback(
                        "Mods.ScreenReaderMod.SmartCursor.UnlockedCursor",
                        "Unlocked cursor");
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
            string? message = hasInteract ? DescribeSmartInteract(out category) : DescribeSmartCursor(player, out category);
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

            // Skip duplicate suppression when holding an axe so the player hears each tree announced
            bool isHoldingAxe = (player?.HeldItem?.axe ?? 0) > 0;
            if (!isHoldingAxe && string.Equals(message, _lastAnnouncement, StringComparison.Ordinal))
            {
                return;
            }

            _lastAnnouncement = message;
            NarrationInstrumentationContext.SetPendingKey(BuildSmartCursorKey(message));
            ScreenReaderService.Announce(message, category: category, force: isHoldingAxe);
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
            _lastCursorAnnouncementKey = int.MinValue;
            _lastIsToggleOn = false;
        }

        private void Reset()
        {
            ResetStateTracking();
            _pendingStatePrefix = null;
            _suppressCursorAnnouncement = false;
        }

        private void ResetSmartCursorRepeatTracking()
        {
            _lastAnnouncement = null;
            _lastCursorAnnouncementKey = int.MinValue;
            _lastCursorTileType = -1;
            _lastTileX = int.MinValue;
            _lastTileY = int.MinValue;
        }

        private void ResetSmartCursorPositionTracking()
        {
            _lastAnnouncement = null;
            _lastTileX = int.MinValue;
            _lastTileY = int.MinValue;
        }

        private static bool ShouldSuppressForMenus(Player player)
        {
            if (AccessibleWireColorMenu.Instance.IsOpen)
            {
                return true;
            }

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
            if (Main.ingameOptionsWindow)
            {
                return true;
            }

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
            if (tileX >= 0 && tileY >= 0 && WorldGen.InWorld(tileX, tileY, 1))
            {
                Tile tile = Main.tile[tileX, tileY];

                // Check for toggle state changes at the same position (levers, timers)
                (bool isToggleable, bool isOn) = GetToggleState(tile);
                bool samePosition = tileX == _lastTileX && tileY == _lastTileY;
                if (isToggleable && samePosition && isOn != _lastIsToggleOn)
                {
                    _lastIsToggleOn = isOn;
                    _lastInteractTileType = tile.TileType;
                    category = AnnouncementCategory.Tile;
                    return GetToggleStateAnnouncement(isOn);
                }

                if (!_descriptorService.TryDescribe(tileX, tileY, out var descriptor))
                {
                    return null;
                }

                if (samePosition && string.Equals(descriptor.Name, _lastAnnouncement, StringComparison.Ordinal))
                {
                    return null;
                }

                if (samePosition && descriptor.TileType == _lastInteractTileType)
                {
                    return null;
                }

                _lastTileX = tileX;
                _lastTileY = tileY;
                _lastNpc = -1;
                _lastProj = -1;
                _lastInteractTileType = descriptor.TileType;
                _lastIsToggleOn = isOn;

                if (!string.IsNullOrWhiteSpace(descriptor.Name))
                {
                    category = descriptor.Category;
                    return descriptor.Name;
                }
            }

            return null;
        }

        private string? DescribeSmartCursor(Player player, out AnnouncementCategory category)
        {
            category = AnnouncementCategory.Default;

            int tileX = Main.SmartCursorX;
            int tileY = Main.SmartCursorY;
            if (!_descriptorService.TryDescribe(tileX, tileY, out var descriptor))
            {
                return null;
            }

            bool suppressedWall = descriptor.IsWall && !ShouldAnnounceWall(player);
            if (suppressedWall)
            {
                ResetSmartCursorPositionTracking();
                return null;
            }

            if (!suppressedWall && descriptor.IsAir)
            {
                ResetSmartCursorPositionTracking();
                return null;
            }

            Tile tile = Main.tile[tileX, tileY];

            // Check for toggle state changes at the same position (levers, timers)
            (bool isToggleable, bool isOn) = GetToggleState(tile);
            bool samePosition = tileX == _lastTileX && tileY == _lastTileY;
            if (isToggleable && samePosition && isOn != _lastIsToggleOn)
            {
                _lastIsToggleOn = isOn;
                _lastCursorTileType = tile.TileType;
                category = AnnouncementCategory.Tile;
                return GetToggleStateAnnouncement(isOn);
            }

            int announcementKey = CursorDescriptorService.ResolveAnnouncementKey(descriptor.TileType, tile);
            if (samePosition && string.Equals(descriptor.Name, _lastAnnouncement, StringComparison.Ordinal))
            {
                return null;
            }

            // Skip repeat suppression when holding an axe so the player hears each tree announced
            bool isHoldingAxe = (player?.HeldItem?.axe ?? 0) > 0;
            if (!isHoldingAxe && announcementKey == _lastCursorAnnouncementKey)
            {
                return null;
            }

            _lastTileX = tileX;
            _lastTileY = tileY;
            _lastNpc = -1;
            _lastProj = -1;
            _lastCursorTileType = descriptor.TileType;
            _lastCursorAnnouncementKey = announcementKey;
            _lastIsToggleOn = isOn;

            if (string.IsNullOrWhiteSpace(descriptor.Name))
            {
                return null;
            }

            category = descriptor.Category;

            // Prepend actuator and wire info when holding wiring tools
            if (IsHoldingWiringTool(player) && WorldGen.InWorld(tileX, tileY, 1))
            {
                string? actuatorPrefix = TileStateDescriptorService.FormatExistingActuator(tile.HasActuator, tile.IsActuated);
                string? wirePrefix = TileStateDescriptorService.FormatExistingWires(
                    tile.RedWire, tile.GreenWire, tile.BlueWire, tile.YellowWire);

                bool hasActuatorPrefix = !string.IsNullOrEmpty(actuatorPrefix);

                // Combine: "actuator on, Red wire" or just "actuator off" or just "Red wire"
                string? mechanicsPrefix = null;
                if (hasActuatorPrefix && !string.IsNullOrEmpty(wirePrefix))
                {
                    mechanicsPrefix = $"{actuatorPrefix}, {wirePrefix}";
                }
                else if (hasActuatorPrefix)
                {
                    mechanicsPrefix = actuatorPrefix;
                }
                else if (!string.IsNullOrEmpty(wirePrefix))
                {
                    mechanicsPrefix = wirePrefix;
                }

                if (!string.IsNullOrEmpty(mechanicsPrefix))
                {
                    // Strip redundant "has actuator" suffix if we're showing the prefix
                    string tileName = hasActuatorPrefix ? StripActuatorSuffix(descriptor.Name) : descriptor.Name;
                    return $"{mechanicsPrefix}, {tileName}";
                }
            }

            return descriptor.Name;
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
            NarrationInstrumentationContext.SetPendingKey(BuildSmartCursorKey(prefix));
            ScreenReaderService.Announce(prefix, force: force);
        }

        private static string BuildSmartCursorKey(string? message)
        {
            string normalized = GlyphTagFormatter.Normalize(message ?? string.Empty).Trim();
            if (normalized.Length > 120)
            {
                normalized = normalized[..120];
            }

            return $"smart:{normalized}";
        }

        /// <summary>
        /// Strips the ", has actuator" suffix from a tile name to avoid redundancy
        /// when the actuator status is already shown as a prefix.
        /// </summary>
        private static string StripActuatorSuffix(string name)
        {
            const string suffix = ", has actuator";
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return name[..^suffix.Length];
            }
            return name;
        }

        /// <summary>
        /// Gets the toggle state for a tile. Returns (isToggleable, isOn).
        /// Handles levers and timers. Doors are excluded since they have audible
        /// sound effects in-game; their state is still shown when focused.
        /// </summary>
        private static (bool isToggleable, bool isOn) GetToggleState(Tile tile)
        {
            if (!tile.HasTile)
            {
                return (false, false);
            }

            // Lever (TileID 132): frameX 0-35 = OFF, frameX 36+ = ON
            if (tile.TileType == TileID.Lever)
            {
                return (true, tile.TileFrameX >= 36);
            }

            // Timer (TileID 144): frameY 0 = OFF, frameY 18 = ON (ticking)
            if (tile.TileType == TileID.Timers)
            {
                return (true, tile.TileFrameY != 0);
            }

            return (false, false);
        }

        /// <summary>
        /// Gets the appropriate toggle state announcement for toggleable tiles like levers and timers.
        /// </summary>
        private static string GetToggleStateAnnouncement(bool isOn)
        {
            return isOn ? "on" : "off";
        }

        private static string GetLocalized(string key)
        {
            string fullKey = $"Mods.ScreenReaderMod.{key}";
            string value = Language.GetTextValue(fullKey);
            if (string.Equals(value, fullKey, StringComparison.Ordinal))
            {
                int lastDot = key.LastIndexOf('.');
                return lastDot >= 0 ? key[(lastDot + 1)..] : key;
            }
            return value;
        }

    }

    private static bool ShouldAnnounceWall(Player player)
    {
        Item held = player?.HeldItem ?? new Item();
        if (held is null || held.IsAir)
        {
            return false;
        }

        if (held.hammer > 0)
        {
            return true;
        }

        return held.createWall > WallID.None;
    }
}
