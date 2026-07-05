#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.Utilities;
using TerrariaAccess.Common;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.GamepadEmulation;
using TerrariaAccess.Common.Systems.MenuNarration;
using TerrariaAccess.Common.Utilities;
using AnnouncementCategory = TerrariaAccess.Common.Services.ScreenReaderService.AnnouncementCategory;
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
    private sealed class CursorNarrator
    {
        private readonly CursorDescriptorService _descriptorService;
        private int _lastTileX = int.MinValue;
        private int _lastTileY = int.MinValue;
        private bool _lastSmartCursorActive;
        private bool _justTransitionedToTileByTile;
        private bool _wasHoveringPlayer;
        private PlayerBodyPart? _lastHoveredBodyPart;
        private int _originTileX = int.MinValue;
        private int _originTileY = int.MinValue;
        private const int MaxActiveCursorSounds = 8;
        private static readonly List<SlotId> ActiveSounds = new();
        private string? _lastTileAnnouncementName;
        private int _lastTileAnnouncementKey = int.MinValue;
        private TileContentSignature _lastTileContentSignature;
        private TileStateSignature _lastTileStateSignature;

        // Entity hover tracking
        private int _lastHoveredNpcIndex = -1;
        private int _lastHoveredOtherPlayerIndex = -1;

        private enum PlayerBodyPart
        {
            Head,
            Torso,
            Legs
        }

        private readonly struct TileContentSignature : IEquatable<TileContentSignature>
        {
            public readonly bool HasTile;
            public readonly ushort TileType;
            public readonly byte LiquidType;
            public readonly ushort WallType;

            public TileContentSignature(int tileX, int tileY)
            {
                if (!WorldGen.InWorld(tileX, tileY, 1))
                {
                    HasTile = false;
                    TileType = 0;
                    LiquidType = 0;
                    WallType = 0;
                    return;
                }

                Tile tile = Main.tile[tileX, tileY];
                HasTile = tile.HasTile;
                TileType = HasTile ? tile.TileType : (ushort)0;
                LiquidType = (byte)tile.LiquidType;
                WallType = tile.WallType;
            }

            public bool Equals(TileContentSignature other) =>
                HasTile == other.HasTile && TileType == other.TileType &&
                LiquidType == other.LiquidType && WallType == other.WallType;

            public override bool Equals(object? obj) => obj is TileContentSignature other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(HasTile, TileType, LiquidType, WallType);
            public static bool operator ==(TileContentSignature left, TileContentSignature right) => left.Equals(right);
            public static bool operator !=(TileContentSignature left, TileContentSignature right) => !left.Equals(right);
        }

        private readonly struct TileStateSignature : IEquatable<TileStateSignature>
        {
            public readonly BlockType BlockType;
            public readonly bool IsActuated;
            public readonly bool HasActuator;
            public readonly bool RedWire;
            public readonly bool GreenWire;
            public readonly bool BlueWire;
            public readonly bool YellowWire;
            public readonly byte TileColor;
            public readonly byte WallColor;
            public readonly bool IsTileInvisible;
            public readonly bool IsWallInvisible;
            public readonly bool IsTileFullbright;
            public readonly bool IsWallFullbright;
            public readonly bool IsToggleableOn;
            public readonly bool IsToggleableTile;
            public readonly int JunctionBoxMode;

            public TileStateSignature(int tileX, int tileY)
            {
                if (!WorldGen.InWorld(tileX, tileY, 1))
                {
                    BlockType = BlockType.Solid;
                    IsActuated = false;
                    HasActuator = false;
                    RedWire = false;
                    GreenWire = false;
                    BlueWire = false;
                    YellowWire = false;
                    TileColor = 0;
                    WallColor = 0;
                    IsTileInvisible = false;
                    IsWallInvisible = false;
                    IsTileFullbright = false;
                    IsWallFullbright = false;
                    IsToggleableOn = false;
                    IsToggleableTile = false;
                    JunctionBoxMode = -1;
                    return;
                }

                Tile tile = Main.tile[tileX, tileY];
                BlockType = tile.HasTile ? tile.BlockType : BlockType.Solid;
                IsActuated = tile.IsActuated;
                HasActuator = tile.HasActuator;
                RedWire = tile.RedWire;
                GreenWire = tile.GreenWire;
                BlueWire = tile.BlueWire;
                YellowWire = tile.YellowWire;
                TileColor = tile.TileColor;
                WallColor = tile.WallColor;
                IsTileInvisible = tile.IsTileInvisible;
                IsWallInvisible = tile.IsWallInvisible;
                IsTileFullbright = tile.IsTileFullbright;
                IsWallFullbright = tile.IsWallFullbright;

                // Track lever/switch toggle state
                (IsToggleableTile, IsToggleableOn) = GetToggleState(tile);

                // Track Junction Box mode (TileID 424 = WirePipe)
                JunctionBoxMode = (tile.HasTile && tile.TileType == TileID.WirePipe)
                    ? tile.TileFrameX / 18
                    : -1;
            }

            private static (bool isToggleable, bool isOn) GetToggleState(Tile tile)
            {
                if (!tile.HasTile)
                {
                    return (false, false);
                }

                // Only track lever state changes - switches are simple buttons
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

                // Logic Sensor (TileID 423): frameX 0 = OFF, frameX 18 = ON (activated)
                if (tile.TileType == TileID.LogicSensor)
                {
                    return (true, tile.TileFrameX >= 18);
                }

                return (false, false);
            }

            public bool Equals(TileStateSignature other) =>
                BlockType == other.BlockType &&
                IsActuated == other.IsActuated &&
                HasActuator == other.HasActuator &&
                RedWire == other.RedWire &&
                GreenWire == other.GreenWire &&
                BlueWire == other.BlueWire &&
                YellowWire == other.YellowWire &&
                TileColor == other.TileColor &&
                WallColor == other.WallColor &&
                IsTileInvisible == other.IsTileInvisible &&
                IsWallInvisible == other.IsWallInvisible &&
                IsTileFullbright == other.IsTileFullbright &&
                IsWallFullbright == other.IsWallFullbright &&
                IsToggleableOn == other.IsToggleableOn &&
                IsToggleableTile == other.IsToggleableTile &&
                JunctionBoxMode == other.JunctionBoxMode;

            public override bool Equals(object? obj) => obj is TileStateSignature other && Equals(other);

            public override int GetHashCode()
            {
                HashCode hash = new();
                hash.Add(BlockType);
                hash.Add(IsActuated);
                hash.Add(HasActuator);
                hash.Add(RedWire);
                hash.Add(GreenWire);
                hash.Add(BlueWire);
                hash.Add(YellowWire);
                hash.Add(TileColor);
                hash.Add(WallColor);
                hash.Add(IsTileInvisible);
                hash.Add(IsWallInvisible);
                hash.Add(IsTileFullbright);
                hash.Add(IsWallFullbright);
                hash.Add(IsToggleableOn);
                hash.Add(IsToggleableTile);
                hash.Add(JunctionBoxMode);
                return hash.ToHashCode();
            }

            public static bool operator ==(TileStateSignature left, TileStateSignature right) => left.Equals(right);
            public static bool operator !=(TileStateSignature left, TileStateSignature right) => !left.Equals(right);
        }

        public CursorNarrator(CursorDescriptorService descriptorService)
        {
            _descriptorService = descriptorService;
        }

        public void Update()
        {
            Player player = Main.LocalPlayer;
            if (player is null || !player.active)
            {
                ResetAll();
                return;
            }

            if (Main.gameMenu || Main.ingameOptionsWindow || Main.InGameUI?.CurrentState is not null ||
                PlayerInput.UsingGamepadUI || AccessibleWireColorMenu.Instance.IsOpen ||
                Main.drawingPlayerChat || Main.editSign || Main.editChest)
            {
                ResetCursorFeedback();
                return;
            }

            bool smartCursorTemporarilySuppressed = DpadVirtualizationSystem.IsTemporarilySuppressingSmartCursor();
            bool smartCursorActive = GamepadEmulationSystem.GetEffectiveSmartCursorState() && !smartCursorTemporarilySuppressed;
            bool gamepadCursorActive = IsGamepadCursorActive();
            bool hasSmartInteract = Main.HasSmartInteractTarget;
            bool canProvideCursorFeedback = !hasSmartInteract || gamepadCursorActive;

            if (_lastSmartCursorActive && !smartCursorActive && canProvideCursorFeedback && !smartCursorTemporarilySuppressed)
            {
                CenterCursorOnPlayer(player);
                _justTransitionedToTileByTile = true;
            }

            _lastSmartCursorActive = smartCursorActive;

            if (!canProvideCursorFeedback)
            {
                ResetCursorFeedback();
                return;
            }

            UpdateOriginFromPlayer(player);

            int tileX;
            int tileY;
            Vector2 tileCenterWorld;
            Vector2 cursorWorld;

            if (smartCursorActive)
            {
                tileX = Main.SmartCursorX;
                tileY = Main.SmartCursorY;

                if (tileX < 0 || tileY < 0)
                {
                    ResetTileTracking();
                    return;
                }

                tileCenterWorld = new Vector2(tileX * 16f + 8f, tileY * 16f + 8f);
                cursorWorld = tileCenterWorld;
            }
            else
            {
                cursorWorld = Main.MouseWorld;
                tileX = (int)(cursorWorld.X / 16f);
                tileY = (int)(cursorWorld.Y / 16f);
                tileCenterWorld = new Vector2(tileX * 16f + 8f, tileY * 16f + 8f);
            }

            if (PlayerInput.UsingGamepadUI && InventoryNarrator.IsInventoryUiOpen(player))
            {
                return;
            }

            bool wasHoveringPlayer = _wasHoveringPlayer;
            bool tileChanged = tileX != _lastTileX || tileY != _lastTileY;
            if (tileChanged)
            {
                PlayCursorCue(player, tileCenterWorld, tileX, tileY);

                _lastTileX = tileX;
                _lastTileY = tileY;
                _lastTileContentSignature = new TileContentSignature(tileX, tileY);
                _lastTileStateSignature = new TileStateSignature(tileX, tileY);
            }

            bool hoveringPlayer = IsHoveringPlayer(player, cursorWorld);
            if (smartCursorActive && hoveringPlayer)
            {
                hoveringPlayer = false;
            }

            TileContentSignature currentSignature = new TileContentSignature(tileX, tileY);
            TileStateSignature currentStateSignature = new TileStateSignature(tileX, tileY);
            bool contentChanged = !tileChanged && currentSignature != _lastTileContentSignature;
            bool stateOnlyChanged = !tileChanged && !contentChanged && currentStateSignature != _lastTileStateSignature;

            if (!gamepadCursorActive && !DpadVirtualizationSystem.AreDpadKeysHeld() && !contentChanged && !stateOnlyChanged && !_justTransitionedToTileByTile)
            {
                _wasHoveringPlayer = hoveringPlayer;
                return;
            }

            if (hoveringPlayer)
            {
                PlayerBodyPart bodyPart = GetHoveredBodyPart(player, cursorWorld);
                bool bodyPartChanged = bodyPart != _lastHoveredBodyPart;

                if (!wasHoveringPlayer || bodyPartChanged || _justTransitionedToTileByTile)
                {
                    AnnouncePlayer(player, bodyPart, cursorWorld);
                    _lastHoveredBodyPart = bodyPart;
                    _justTransitionedToTileByTile = false;
                }

                _wasHoveringPlayer = true;
                return;
            }

            _wasHoveringPlayer = false;
            _lastHoveredBodyPart = null;

            // Check for hovering over other players (multiplayer)
            if (!smartCursorActive)
            {
                int hoveredOtherPlayerIndex = GetHoveredOtherPlayerIndex(player, cursorWorld);
                if (hoveredOtherPlayerIndex >= 0)
                {
                    if (hoveredOtherPlayerIndex != _lastHoveredOtherPlayerIndex || _justTransitionedToTileByTile)
                    {
                        Player otherPlayer = Main.player[hoveredOtherPlayerIndex];
                        AnnounceOtherPlayer(otherPlayer);
                        _lastHoveredOtherPlayerIndex = hoveredOtherPlayerIndex;
                        _lastHoveredNpcIndex = -1;
                        _justTransitionedToTileByTile = false;
                    }

                    return;
                }
            }

            _lastHoveredOtherPlayerIndex = -1;

            // Check for hovering over NPCs
            if (!smartCursorActive)
            {
                int hoveredNpcIndex = GetHoveredNpcIndex(cursorWorld);
                if (hoveredNpcIndex >= 0)
                {
                    if (hoveredNpcIndex != _lastHoveredNpcIndex || _justTransitionedToTileByTile)
                    {
                        NPC npc = Main.npc[hoveredNpcIndex];
                        AnnounceNpc(npc);
                        _lastHoveredNpcIndex = hoveredNpcIndex;
                        _justTransitionedToTileByTile = false;
                    }

                    return;
                }
            }

            _lastHoveredNpcIndex = -1;

            // Handle state-only changes (e.g., slope changed, wire added, paint applied, lever toggled)
            if (stateOnlyChanged)
            {
                List<string> stateChanges = TileStateDescriptorService.GetStateChanges(
                    _lastTileStateSignature.BlockType, _lastTileStateSignature.IsActuated, _lastTileStateSignature.HasActuator,
                    _lastTileStateSignature.RedWire, _lastTileStateSignature.GreenWire, _lastTileStateSignature.BlueWire, _lastTileStateSignature.YellowWire,
                    _lastTileStateSignature.TileColor, _lastTileStateSignature.WallColor,
                    _lastTileStateSignature.IsTileInvisible, _lastTileStateSignature.IsWallInvisible,
                    _lastTileStateSignature.IsTileFullbright, _lastTileStateSignature.IsWallFullbright,
                    _lastTileStateSignature.IsToggleableTile, _lastTileStateSignature.IsToggleableOn,
                    currentStateSignature.BlockType, currentStateSignature.IsActuated, currentStateSignature.HasActuator,
                    currentStateSignature.RedWire, currentStateSignature.GreenWire, currentStateSignature.BlueWire, currentStateSignature.YellowWire,
                    currentStateSignature.TileColor, currentStateSignature.WallColor,
                    currentStateSignature.IsTileInvisible, currentStateSignature.IsWallInvisible,
                    currentStateSignature.IsTileFullbright, currentStateSignature.IsWallFullbright,
                    currentStateSignature.IsToggleableTile, currentStateSignature.IsToggleableOn,
                    _lastTileStateSignature.JunctionBoxMode, currentStateSignature.JunctionBoxMode);

                if (stateChanges.Count > 0)
                {
                    string stateMessage = string.Join(", ", stateChanges);
                    AnnounceCursorMessage(stateMessage, force: true, category: AnnouncementCategory.Tile);
                }

                _lastTileStateSignature = currentStateSignature;
                return;
            }

            bool shouldAnnounceTile = tileChanged || wasHoveringPlayer || contentChanged;
            if (!shouldAnnounceTile)
            {
                return;
            }

            _lastTileContentSignature = currentSignature;
            _lastTileStateSignature = currentStateSignature;

            string coordinates = smartCursorActive ? string.Empty : BuildCoordinateMessage(tileX, tileY);

            if (!_descriptorService.TryDescribe(tileX, tileY, out var descriptor))
            {
                _lastTileAnnouncementName = null;
                return;
            }

            bool isWall = descriptor.IsWall;
            bool suppressedWall = isWall && !ShouldAnnounceWall(player);
            if (suppressedWall)
            {
                descriptor = descriptor with { TileType = -1, Name = "Empty", Category = AnnouncementCategory.Tile, IsWall = false, IsAir = false };
            }

            if (string.IsNullOrWhiteSpace(descriptor.Name))
            {
                _lastTileAnnouncementName = null;
                return;
            }

            if (!smartCursorActive && gamepadCursorActive && !IsGamepadDpadPressed() &&
                string.Equals(descriptor.Name, "Empty", StringComparison.OrdinalIgnoreCase) && !suppressedWall)
            {
                _lastTileAnnouncementName = null;
                _lastTileAnnouncementKey = int.MinValue;
                return;
            }

            int announcementKey = WorldGen.InWorld(tileX, tileY, 1)
                ? CursorDescriptorService.ResolveAnnouncementKey(descriptor.TileType, Main.tile[tileX, tileY], tileX, tileY)
                : CursorDescriptorService.ResolveAnnouncementKey(descriptor.TileType);

            bool suppressRepeats = smartCursorActive || (gamepadCursorActive && !IsGamepadDpadPressed());
            if (suppressRepeats &&
                string.Equals(descriptor.Name, _lastTileAnnouncementName, StringComparison.Ordinal) &&
                announcementKey == _lastTileAnnouncementKey)
            {
                return;
            }

            if (suppressRepeats &&
                announcementKey == _lastTileAnnouncementKey &&
                CursorDescriptorService.ShouldSuppressVariantNames(announcementKey))
            {
                return;
            }

            _lastTileAnnouncementKey = announcementKey;
            _lastTileAnnouncementName = descriptor.Name;

            if (smartCursorActive)
            {
                return;
            }

            // Prepend actuator and wire info when holding wiring tools
            string? mechanicsPrefix = null;
            bool hasActuatorPrefix = false;
            if (IsHoldingWiringTool(player) && WorldGen.InWorld(tileX, tileY, 1))
            {
                Tile tile = Main.tile[tileX, tileY];
                string? actuatorPrefix = TileStateDescriptorService.FormatExistingActuator(tile.HasActuator, tile.IsActuated);
                string? wirePrefix = TileStateDescriptorService.FormatExistingWires(
                    tile.RedWire, tile.GreenWire, tile.BlueWire, tile.YellowWire);

                hasActuatorPrefix = !string.IsNullOrEmpty(actuatorPrefix);

                // Combine: "actuator on, Red wire" or just "actuator off" or just "Red wire"
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
            }

            // Get the tile name, stripping redundant "has actuator" suffix if we're showing the prefix
            string tileName = descriptor.Name;
            if (hasActuatorPrefix)
            {
                tileName = StripActuatorSuffix(tileName);
            }

            string message;
            if (!string.IsNullOrEmpty(mechanicsPrefix))
            {
                message = string.IsNullOrWhiteSpace(coordinates)
                    ? $"{mechanicsPrefix}, {tileName}"
                    : $"{mechanicsPrefix}, {tileName}, {coordinates}";
            }
            else
            {
                message = string.IsNullOrWhiteSpace(coordinates) ? tileName : $"{tileName}, {coordinates}";
            }

            AnnouncementCategory category = descriptor.Category;
            AnnounceCursorMessage(message, force: true, category: category);
            _justTransitionedToTileByTile = false;
        }

        private void ResetAll()
        {
            ResetCursorFeedback();
            _lastSmartCursorActive = false;
            _justTransitionedToTileByTile = false;
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
            _lastHoveredBodyPart = null;
            _originTileX = int.MinValue;
            _originTileY = int.MinValue;
            _lastTileAnnouncementName = null;
            _lastTileAnnouncementKey = int.MinValue;
            _lastTileContentSignature = default;
            _lastTileStateSignature = default;
            _lastHoveredNpcIndex = -1;
            _lastHoveredOtherPlayerIndex = -1;
        }

        private static bool IsHoveringPlayer(Player player, Vector2 cursorWorld)
        {
            Rectangle bounds = player.getRect();
            bounds.Inflate(4, 4);
            return bounds.Contains((int)cursorWorld.X, (int)cursorWorld.Y);
        }

        private static PlayerBodyPart GetHoveredBodyPart(Player player, Vector2 cursorWorld)
        {
            Rectangle bounds = player.getRect();
            bounds.Inflate(4, 4);

            float relativeY = cursorWorld.Y - bounds.Top;
            float third = bounds.Height / 3f;

            if (relativeY < third)
            {
                return PlayerBodyPart.Head;
            }
            else if (relativeY < third * 2)
            {
                return PlayerBodyPart.Torso;
            }
            else
            {
                return PlayerBodyPart.Legs;
            }
        }

        /// <summary>
        /// Checks if the cursor is hovering over any NPC and returns the index if found.
        /// </summary>
        private static int GetHoveredNpcIndex(Vector2 cursorWorld)
        {
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (!npc.active)
                {
                    continue;
                }

                Rectangle bounds = npc.getRect();
                bounds.Inflate(4, 4);
                if (bounds.Contains((int)cursorWorld.X, (int)cursorWorld.Y))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Checks if the cursor is hovering over any other player (not the local player) and returns the index if found.
        /// </summary>
        private static int GetHoveredOtherPlayerIndex(Player localPlayer, Vector2 cursorWorld)
        {
            if (Main.netMode == NetmodeID.SinglePlayer)
            {
                return -1;
            }

            for (int i = 0; i < Main.maxPlayers; i++)
            {
                Player other = Main.player[i];
                if (!other.active || other.dead || other.ghost || other == localPlayer)
                {
                    continue;
                }

                Rectangle bounds = other.getRect();
                bounds.Inflate(4, 4);
                if (bounds.Contains((int)cursorWorld.X, (int)cursorWorld.Y))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Announces information about the NPC under the cursor.
        /// </summary>
        private void AnnounceNpc(NPC npc)
        {
            string name = ResolveNpcDisplayName(npc);
            List<string> parts = new() { name };

            // Add health info for non-critters that have health
            if (npc.lifeMax > 0 && !NPCID.Sets.CountsAsCritter[npc.type])
            {
                string healthText = LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.EntityInfo.Health",
                    "{0} of {1} health");
                parts.Add(string.Format(healthText, npc.life, npc.lifeMax));
            }

            // Indicate if friendly or hostile (for non-town NPCs)
            if (!npc.townNPC && !NPCID.Sets.ActsLikeTownNPC[npc.type] && !NPCID.Sets.IsTownPet[npc.type])
            {
                if (npc.friendly)
                {
                    parts.Add(LocalizationHelper.GetTextOrFallback(
                        "Mods.TerrariaAccess.EntityInfo.Friendly",
                        "friendly"));
                }
                else
                {
                    parts.Add(LocalizationHelper.GetTextOrFallback(
                        "Mods.TerrariaAccess.EntityInfo.Hostile",
                        "hostile"));
                }
            }

            string announcement = string.Join(", ", parts);
            AnnounceCursorContextMessage(announcement);
        }

        /// <summary>
        /// Announces information about another player under the cursor (multiplayer only).
        /// </summary>
        private void AnnounceOtherPlayer(Player other)
        {
            string name = !string.IsNullOrWhiteSpace(other.name) ? other.name : "Player";
            List<string> parts = new() { name };

            // Health
            string healthText = LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.EntityInfo.Health",
                "{0} of {1} health");
            parts.Add(string.Format(healthText, other.statLife, other.statLifeMax2));

            // Mana (only if they have mana)
            if (other.statManaMax2 > 0)
            {
                string manaText = LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.EntityInfo.Mana",
                    "{0} of {1} mana");
                parts.Add(string.Format(manaText, other.statMana, other.statManaMax2));
            }

            // Defense
            string defenseText = LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.EntityInfo.Defense",
                "{0} defense");
            parts.Add(string.Format(defenseText, other.statDefense));

            // Team (if on a team)
            if (other.team > 0)
            {
                string? teamName = other.team switch
                {
                    1 => LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.InventorySpecial.TeamRed", "Red team"),
                    2 => LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.InventorySpecial.TeamGreen", "Green team"),
                    3 => LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.InventorySpecial.TeamBlue", "Blue team"),
                    4 => LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.InventorySpecial.TeamYellow", "Yellow team"),
                    5 => LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.InventorySpecial.TeamPink", "Pink team"),
                    _ => null
                };

                if (teamName != null)
                {
                    parts.Add(teamName);
                }
            }

            string announcement = string.Join(", ", parts);
            AnnounceCursorContextMessage(announcement);
        }

        /// <summary>
        /// Resolves the display name for an NPC.
        /// </summary>
        private static string ResolveNpcDisplayName(NPC npc)
        {
            if (!string.IsNullOrWhiteSpace(npc.FullName))
            {
                return npc.FullName;
            }

            if (!string.IsNullOrWhiteSpace(npc.GivenName))
            {
                return npc.GivenName;
            }

            string localized = Lang.GetNPCNameValue(npc.type);
            if (!string.IsNullOrWhiteSpace(localized))
            {
                return localized;
            }

            return "NPC";
        }

        private void AnnouncePlayer(Player player, PlayerBodyPart bodyPart, Vector2 cursorWorld)
        {
            string partName = bodyPart switch
            {
                PlayerBodyPart.Head => "Head",
                PlayerBodyPart.Torso => "Torso",
                PlayerBodyPart.Legs => "Legs",
                _ => "Body"
            };

            // Calculate vertical offset from ground level
            string offsetText = "";
            if (_originTileY != int.MinValue)
            {
                int cursorTileY = (int)(cursorWorld.Y / 16f);
                int verticalOffset = _originTileY - cursorTileY;
                if (verticalOffset > 0)
                {
                    offsetText = $", {verticalOffset} up";
                }
                else if (verticalOffset < 0)
                {
                    offsetText = $", {Math.Abs(verticalOffset)} down";
                }
            }

            string announcement = $"{player.name}'s {partName}{offsetText}";
            AnnounceCursorContextMessage(announcement);
        }

        private static void CenterCursorOnPlayer(Player player)
        {
            // Calculate tile coordinates at the player's feet (the ground tile they're standing on)
            int groundTileX = (int)(player.Center.X / 16f);
            int groundTileY = (int)(player.Bottom.Y / 16f);

            // Position cursor at the center of the ground tile (tiles are always 16x16 world units)
            Vector2 groundTileCenter = new Vector2(groundTileX * 16f + 8f, groundTileY * 16f + 8f);
            Vector2 screenSpace = groundTileCenter - Main.screenPosition;

            int centeredX = (int)MathHelper.Clamp(screenSpace.X, 0f, Main.screenWidth - 1);
            int centeredY = (int)MathHelper.Clamp(screenSpace.Y, 0f, Main.screenHeight - 1);

            Main.mouseX = centeredX;
            Main.mouseY = centeredY;
            PlayerInput.MouseX = centeredX;
            PlayerInput.MouseY = centeredY;
        }

        private void UpdateOriginFromPlayer(Player player)
        {
            // Use the tile below the player's feet as the origin (ground level)
            _originTileX = (int)(player.Center.X / 16f);
            _originTileY = (int)(player.Bottom.Y / 16f);
        }

        private static void AnnounceCursorMessage(string message, bool force, AnnouncementCategory category = AnnouncementCategory.Default)
        {
            (string? modePrefix, _) = ConsumePendingCursorModeAnnouncement();
            string effectiveMessage = string.IsNullOrWhiteSpace(modePrefix)
                ? message
                : $"{modePrefix}. {message}";
            string messageKey = NormalizeKey(effectiveMessage);

            if (HotbarNarrator.TryDequeuePendingAnnouncement(out string hotbarAnnouncement, out string? hotbarKey))
            {
                string combined = string.IsNullOrWhiteSpace(hotbarAnnouncement)
                    ? effectiveMessage
                    : $"{hotbarAnnouncement}. {effectiveMessage}";

                NarrationInstrumentationContext.SetPendingKey(hotbarKey ?? $"cursor:{messageKey}");
                ScreenReaderService.Announce(combined, force: force, category: category);
                return;
            }

            NarrationInstrumentationContext.SetPendingKey($"cursor:{messageKey}");
            ScreenReaderService.Announce(effectiveMessage, force: force, category: category);
        }

        private static void AnnounceCursorContextMessage(string message)
        {
            (string? modePrefix, _) = ConsumePendingCursorModeAnnouncement();
            string effectiveMessage = string.IsNullOrWhiteSpace(modePrefix)
                ? message
                : $"{modePrefix}. {message}";

            NarrationInstrumentationContext.SetPendingKey($"cursor:{NormalizeKey(effectiveMessage)}");
            ScreenReaderService.Announce(effectiveMessage, force: true);
        }

        private static string NormalizeKey(string text)
        {
            string normalized = GlyphTagFormatter.Normalize(text ?? string.Empty).Trim();
            if (normalized.Length > 120)
            {
                normalized = normalized[..120];
            }

            return normalized;
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

        private string BuildCoordinateMessage(int tileX, int tileY)
        {
            if (_originTileX == int.MinValue || _originTileY == int.MinValue)
            {
                return string.Empty;
            }

            // When build mode is awaiting second corner, use "width by height" format
            // instead of directional coordinates for easier mental mapping of selection area.
            var buildModePlayer = Main.LocalPlayer?.GetModPlayer<Players.BuildModePlayer>();
            if (buildModePlayer?.IsAwaitingSecondCorner == true)
            {
                return buildModePlayer.GetSelectionDimensions(tileX, tileY);
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
                return "ground level";
            }

            return string.Join(", ", parts);
        }

        private static void PlayCursorCue(Player player, Vector2 tileCenterWorld, int tileX, int tileY)
        {
            // Check if cursor tile sounds are enabled in config
            if (!(TerrariaAccessConfig.Instance?.CursorTileSounds ?? true))
            {
                return;
            }

            CleanupFinishedInstances();
            if (ActiveSounds.Count >= MaxActiveCursorSounds)
            {
                return;
            }

            float baseVolume = 0.45f;
            SpatializedSoundEngine.SpatialAudioSample sample = SpatializedSoundEngine.Compute(
                player.Center,
                tileCenterWorld,
                baseVolume);
            float configVolume = TerrariaAccessConfig.Instance?.CursorVolume ?? 1f;
            if (!sample.IsAudible(configVolume))
            {
                return;
            }

            if (!TryResolveCursorSound(tileX, tileY, out SoundStyle style, out float localVolumeScale, out float pitchOffset))
            {
                return;
            }

            float volume = sample.ScaleVolume(configVolume * localVolumeScale) * AudioVolumeDefaults.WorldCueVolumeScale;
            if (volume <= 0f)
            {
                return;
            }

            float pan = MathHelper.Clamp(sample.NormalizedScreenX, -1f, 1f);
            float pitch = MathHelper.Clamp(sample.Pitch + pitchOffset, -1f, 1f);
            SlotId slot = SoundEngine.PlaySound(
                style with { MaxInstances = 0 },
                position: null,
                sound =>
                {
                    sound.Position = null;
                    sound.Volume = volume;
                    sound.Pitch = pitch;
                    if (sound.Sound is not null && !sound.Sound.IsDisposed)
                    {
                        sound.Sound.Pan = pan;
                    }

                    return true;
                });

            if (slot.IsValid)
            {
                ActiveSounds.Add(slot);
            }
        }

        public static void DisposeStaticResources()
        {
            for (int i = ActiveSounds.Count - 1; i >= 0; i--)
            {
                if (SoundEngine.TryGetActiveSound(ActiveSounds[i], out ActiveSound? activeSound))
                {
                    activeSound.Stop();
                }
            }

            ActiveSounds.Clear();
        }

        private static bool TryResolveCursorSound(
            int tileX,
            int tileY,
            out SoundStyle style,
            out float volumeScale,
            out float pitchOffset)
        {
            style = default;
            volumeScale = 1f;
            pitchOffset = 0f;

            if (!WorldGen.InWorld(tileX, tileY, 1))
            {
                return false;
            }

            Tile tile = Main.tile[tileX, tileY];
            if (tile.LiquidAmount > 0 && TryResolveLiquidSound(tile, out style, out volumeScale, out pitchOffset))
            {
                return true;
            }

            if (tile.HasTile)
            {
                return TryResolveTileHitSound(tile, out style, out volumeScale, out pitchOffset);
            }

            if (tile.WallType > WallID.None)
            {
                return TryResolveWallHitSound(tile, out style, out volumeScale, out pitchOffset);
            }

            return false;
        }

        private static bool TryResolveLiquidSound(Tile tile, out SoundStyle style, out float volumeScale, out float pitchOffset)
        {
            switch (tile.LiquidType)
            {
                case LiquidID.Lava:
                    style = SoundID.Splash;
                    volumeScale = 0.75f;
                    pitchOffset = -0.22f;
                    return true;
                case LiquidID.Honey:
                    style = SoundID.SplashWeak;
                    volumeScale = 0.7f;
                    pitchOffset = -0.12f;
                    return true;
                case LiquidID.Shimmer:
                    style = Main.rand.NextBool() ? SoundID.ShimmerWeak1 : SoundID.ShimmerWeak2;
                    volumeScale = 0.75f;
                    pitchOffset = -0.08f;
                    return true;
                case LiquidID.Water:
                    style = tile.LiquidAmount < 128 ? SoundID.SplashWeak : SoundID.Splash;
                    volumeScale = 0.65f + MathHelper.Clamp(tile.LiquidAmount / 255f, 0f, 1f) * 0.35f;
                    pitchOffset = 0f;
                    return true;
                default:
                    style = default;
                    volumeScale = 0f;
                    pitchOffset = 0f;
                    return false;
            }
        }

        private static bool TryResolveTileHitSound(Tile tile, out SoundStyle style, out float volumeScale, out float pitchOffset)
        {
            volumeScale = 1f;
            pitchOffset = 0f;

            ModTile? modTile = TileLoader.GetTile(tile.TileType);
            if (modTile is not null)
            {
                if (modTile.HitSound is SoundStyle modHitSound)
                {
                    style = modHitSound;
                    return true;
                }
            }

            int type = tile.TileType;
            if (type == TileID.Crystals || type == TileID.LargePiles2)
            {
                style = SoundID.Item27;
                return true;
            }

            if (type == TileID.Traps || type == TileID.PlanterBox)
            {
                style = Main.rand.NextBool() ? SoundID.Item48 : SoundID.Item49;
                return true;
            }

            if (IsWoodLikeTile(type))
            {
                style = SoundID.Dig;
                volumeScale = 0.85f;
                pitchOffset = -0.1f;
                return true;
            }

            if (IsPlantLikeTile(type))
            {
                style = SoundID.Grass;
                volumeScale = 0.8f;
                return true;
            }

            if (type == TileID.MinecartTrack)
            {
                style = SoundID.Item52;
                return true;
            }

            if (type == TileID.Chimney || type == TileID.Explosives || type == TileID.Grate)
            {
                style = SoundID.NPCHit4;
                return true;
            }

            if (type == TileID.ClosedDoor && tile.TileFrameX >= 54)
            {
                style = SoundID.NPCHit4;
                return true;
            }

            if (IsGlassLikeTile(type))
            {
                style = SoundID.Shatter;
                volumeScale = 0.8f;
                return true;
            }

            if (IsMetalLikeTile(type))
            {
                style = SoundID.Tink;
                volumeScale = 0.75f;
                pitchOffset = 0.08f;
                return true;
            }

            if (type == TileID.Pots || type == TileID.PotsSuspended)
            {
                style = SoundID.Dig;
                volumeScale = 0.9f;
                return true;
            }

            if (type == TileID.BreakableIce)
            {
                style = SoundID.Item27;
                volumeScale = 0.85f;
                return true;
            }

            if ((type == TileID.MagicalIceBlock || type == TileID.ShimmerBlock || type == TileID.Larva) &&
                !Main.tileSolid[type])
            {
                style = SoundID.Item27;
                volumeScale = 0.85f;
                pitchOffset = 0.08f;
                return true;
            }

            if (IsIceLikeTile(type))
            {
                style = SoundID.Item50;
                volumeScale = 0.85f;
                pitchOffset = -0.08f;
                return true;
            }

            if (IsSnowLikeTile(type))
            {
                style = Main.rand.NextBool() ? SoundID.Item48 : SoundID.Item49;
                volumeScale = 0.75f;
                pitchOffset = 0.05f;
                return true;
            }

            if (IsGrassBlockTile(type))
            {
                style = SoundID.Grass;
                volumeScale = 0.75f;
                pitchOffset = -0.05f;
                return true;
            }

            if (IsSandLikeTile(type))
            {
                style = SoundID.Dig;
                volumeScale = 0.65f;
                pitchOffset = 0.18f;
                return true;
            }

            if (IsOrganicLikeTile(type))
            {
                style = SoundID.Dig;
                volumeScale = 0.75f;
                pitchOffset = -0.08f;
                return true;
            }

            if (IsDirtLikeTile(type))
            {
                style = SoundID.Dig;
                volumeScale = 0.72f;
                pitchOffset = 0.08f;
                return true;
            }

            if (IsHardStoneLikeTile(type))
            {
                style = SoundID.Tink;
                volumeScale = 0.7f;
                pitchOffset = -0.16f;
                return true;
            }

            if (IsBrickLikeTile(type))
            {
                style = SoundID.Tink;
                volumeScale = 0.78f;
                pitchOffset = -0.08f;
                return true;
            }

            if (IsSolidFullTile(type))
            {
                style = SoundID.Dig;
                volumeScale = 0.85f;
                pitchOffset = -0.03f;
                return true;
            }

            style = SoundID.Dig;
            volumeScale = 0.8f;
            return true;
        }

        private static bool TryResolveWallHitSound(Tile tile, out SoundStyle style, out float volumeScale, out float pitchOffset)
        {
            volumeScale = 1f;
            pitchOffset = 0f;

            ModWall? modWall = WallLoader.GetWall(tile.WallType);
            if (modWall is not null)
            {
                if (modWall.HitSound is SoundStyle modHitSound)
                {
                    style = modHitSound;
                    return true;
                }
            }

            int wall = tile.WallType;
            if (wall == WallID.Glass ||
                wall == WallID.BlueStainedGlass ||
                wall == WallID.GreenStainedGlass ||
                wall == WallID.PurpleStainedGlass ||
                wall == WallID.RedStainedGlass ||
                wall == WallID.YellowStainedGlass ||
                wall == WallID.RainbowStainedGlass ||
                wall == WallID.Confetti ||
                wall == WallID.ConfettiBlack)
            {
                style = SoundID.Shatter;
                volumeScale = 0.75f;
                return true;
            }

            if ((wall >= WallID.CopperPlating && wall <= WallID.TinPlating) ||
                wall == WallID.IronFence ||
                wall == WallID.MetalFence ||
                wall == WallID.WroughtIronFence ||
                wall == WallID.LunarRustBrickWall)
            {
                style = SoundID.Tink;
                volumeScale = 0.75f;
                pitchOffset = 0.08f;
                return true;
            }

            if (WallSetContains(WallID.Sets.Conversion.Ice, wall))
            {
                style = SoundID.Item50;
                volumeScale = 0.75f;
                pitchOffset = -0.08f;
                return true;
            }

            if (WallSetContains(WallID.Sets.Conversion.Snow, wall))
            {
                style = Main.rand.NextBool() ? SoundID.Item48 : SoundID.Item49;
                volumeScale = 0.7f;
                pitchOffset = 0.05f;
                return true;
            }

            if (WallSetContains(WallID.Sets.Conversion.Grass, wall))
            {
                style = SoundID.Grass;
                volumeScale = 0.7f;
                pitchOffset = -0.05f;
                return true;
            }

            if (WallSetContains(WallID.Sets.Conversion.PureSand, wall) ||
                WallSetContains(WallID.Sets.Conversion.HardenedSand, wall) ||
                WallSetContains(WallID.Sets.Conversion.Sandstone, wall))
            {
                style = SoundID.Dig;
                volumeScale = 0.65f;
                pitchOffset = 0.15f;
                return true;
            }

            if (WallSetContains(WallID.Sets.Conversion.Dirt, wall))
            {
                style = SoundID.Dig;
                volumeScale = 0.7f;
                pitchOffset = 0.08f;
                return true;
            }

            if (WallSetContains(WallID.Sets.Conversion.Stone, wall) ||
                WallSetContains(WallID.Sets.Conversion.NewWall1, wall) ||
                WallSetContains(WallID.Sets.Conversion.NewWall2, wall) ||
                WallSetContains(WallID.Sets.Conversion.NewWall3, wall) ||
                WallSetContains(WallID.Sets.Conversion.NewWall4, wall) ||
                MainWallSetContains(Main.wallDungeon, wall))
            {
                style = SoundID.Tink;
                volumeScale = 0.7f;
                pitchOffset = -0.12f;
                return true;
            }

            style = SoundID.Dig;
            volumeScale = 0.75f;
            return true;
        }

        private static bool IsGlassLikeTile(int type)
        {
            return type == TileID.Glass ||
                type == TileID.GlassKiln ||
                type == TileID.Waterfall ||
                type == TileID.Lavafall ||
                type == TileID.Confetti ||
                type == TileID.ConfettiBlack ||
                type == TileID.Bubble ||
                type == TileID.CrystalBlock ||
                type == TileID.SandFallBlock ||
                type == TileID.SnowFallBlock;
        }

        private static bool IsIceLikeTile(int type)
        {
            return TileSetContains(TileID.Sets.Conversion.Ice, type) ||
                TileSetContains(TileID.Sets.IceSkateSlippery, type) ||
                type == TileID.IceBlock ||
                type == TileID.CorruptIce ||
                type == TileID.HallowedIce ||
                type == TileID.FleshIce ||
                type == TileID.FrozenSlimeBlock ||
                type == TileID.MagicalIceBlock;
        }

        private static bool IsSnowLikeTile(int type)
        {
            return TileSetContains(TileID.Sets.Conversion.Snow, type) ||
                type == TileID.SnowBlock ||
                type == TileID.SnowBrick ||
                type == TileID.Slush ||
                type == TileID.SnowCloud;
        }

        private static bool IsSandLikeTile(int type)
        {
            return MainTileSetContains(Main.tileSand, type) ||
                TileSetContains(TileID.Sets.Conversion.Sand, type) ||
                TileSetContains(TileID.Sets.Conversion.HardenedSand, type) ||
                TileSetContains(TileID.Sets.Conversion.Sandstone, type) ||
                TileSetContains(TileID.Sets.isDesertBiomeSand, type) ||
                type == TileID.Sand ||
                type == TileID.Ebonsand ||
                type == TileID.Pearlsand ||
                type == TileID.Crimsand ||
                type == TileID.Silt ||
                type == TileID.HardenedSand ||
                type == TileID.Sandstone ||
                type == TileID.SandstoneBrick ||
                type == TileID.SandStoneSlab ||
                type == TileID.SandstoneColumn;
        }

        private static bool IsGrassBlockTile(int type)
        {
            return TileSetContains(TileID.Sets.Conversion.Grass, type) ||
                TileSetContains(TileID.Sets.Conversion.JungleGrass, type) ||
                TileSetContains(TileID.Sets.Conversion.MushroomGrass, type) ||
                TileSetContains(TileID.Sets.Conversion.GolfGrass, type) ||
                TileSetContains(TileID.Sets.Grass, type) ||
                type == TileID.Grass ||
                type == TileID.CorruptGrass ||
                type == TileID.JungleGrass ||
                type == TileID.MushroomGrass ||
                type == TileID.HallowedGrass ||
                type == TileID.CrimsonGrass ||
                type == TileID.AshGrass;
        }

        private static bool IsDirtLikeTile(int type)
        {
            return TileSetContains(TileID.Sets.Conversion.Dirt, type) ||
                TileSetContains(TileID.Sets.CanBeDugByShovel, type) ||
                type == TileID.Dirt ||
                type == TileID.ClayBlock ||
                type == TileID.DirtiestBlock;
        }

        private static bool IsOrganicLikeTile(int type)
        {
            return type == TileID.Mud ||
                type == TileID.Mudstone ||
                type == TileID.Ash ||
                type == TileID.CactusBlock ||
                type == TileID.MushroomBlock ||
                type == TileID.SlimeBlock ||
                type == TileID.PinkSlimeBlock ||
                type == TileID.FleshBlock ||
                type == TileID.HoneyBlock ||
                type == TileID.CrispyHoneyBlock ||
                type == TileID.Hive ||
                type == TileID.Cloud ||
                type == TileID.RainCloud ||
                type == TileID.LeafBlock;
        }

        private static bool IsHardStoneLikeTile(int type)
        {
            return MainTileSetContains(Main.tileStone, type) ||
                TileSetContains(TileID.Sets.Conversion.Stone, type) ||
                TileSetContains(TileID.Sets.Stone, type) ||
                TileSetContains(TileID.Sets.Ore, type) ||
                MainTileOreFinderPriority(type) > 0 ||
                type == TileID.Stone ||
                type == TileID.Ebonstone ||
                type == TileID.Pearlstone ||
                type == TileID.Crimstone ||
                type == TileID.Obsidian ||
                type == TileID.Marble ||
                type == TileID.MarbleBlock ||
                type == TileID.MarbleColumn ||
                type == TileID.Granite ||
                type == TileID.GraniteBlock ||
                type == TileID.GraniteColumn ||
                type == TileID.StoneSlab ||
                type == TileID.ActiveStoneBlock ||
                type == TileID.InactiveStoneBlock ||
                type == TileID.Boulder;
        }

        private static bool IsBrickLikeTile(int type)
        {
            return MainTileSetContains(Main.tileBrick, type) ||
                type == TileID.GrayBrick ||
                type == TileID.RedBrick ||
                type == TileID.BlueDungeonBrick ||
                type == TileID.GreenDungeonBrick ||
                type == TileID.PinkDungeonBrick ||
                type == TileID.DemoniteBrick ||
                type == TileID.ObsidianBrick ||
                type == TileID.HellstoneBrick ||
                type == TileID.PearlstoneBrick ||
                type == TileID.EbonstoneBrick ||
                type == TileID.CrimstoneBrick ||
                type == TileID.LihzahrdBrick ||
                type == TileID.ChlorophyteBrick;
        }

        private static bool IsSolidFullTile(int type)
        {
            return MainTileSetContains(Main.tileSolid, type) &&
                !MainTileSetContains(Main.tileSolidTop, type);
        }

        private static bool TileSetContains(bool[] set, int type)
        {
            return type >= 0 && type < set.Length && set[type];
        }

        private static bool MainTileSetContains(bool[] set, int type)
        {
            return type >= 0 && type < set.Length && set[type];
        }

        private static bool WallSetContains(bool[] set, int wall)
        {
            return wall >= 0 && wall < set.Length && set[wall];
        }

        private static bool MainWallSetContains(bool[] set, int wall)
        {
            return wall >= 0 && wall < set.Length && set[wall];
        }

        private static short MainTileOreFinderPriority(int type)
        {
            return type >= 0 && type < Main.tileOreFinderPriority.Length
                ? Main.tileOreFinderPriority[type]
                : (short)0;
        }

        private static bool IsWoodLikeTile(int type)
        {
            return (type >= 0 &&
                    type < TileID.Sets.IsATreeTrunk.Length &&
                    TileID.Sets.IsATreeTrunk[type]) ||
                type == TileID.Trees ||
                type == TileID.PalmTree ||
                type == TileID.PineTree ||
                type == TileID.VanityTreeSakura ||
                type == TileID.VanityTreeYellowWillow ||
                type == TileID.TreeAsh ||
                type == TileID.TreeTopaz ||
                type == TileID.TreeAmethyst ||
                type == TileID.TreeSapphire ||
                type == TileID.TreeEmerald ||
                type == TileID.TreeRuby ||
                type == TileID.TreeDiamond ||
                type == TileID.TreeAmber ||
                type == TileID.Bamboo ||
                type == TileID.BambooBlock ||
                type == TileID.Cactus ||
                type == TileID.CactusBlock ||
                type == TileID.MushroomTrees ||
                type == TileID.WoodBlock ||
                type == TileID.LivingWood;
        }

        private static bool IsPlantLikeTile(int type)
        {
            return Main.tileAlch[type] ||
                type == TileID.Plants ||
                type == TileID.Plants2 ||
                type == TileID.CorruptPlants ||
                type == TileID.CrimsonPlants ||
                type == TileID.HallowedPlants ||
                type == TileID.HallowedPlants2 ||
                type == TileID.JunglePlants ||
                type == TileID.JunglePlants2 ||
                type == TileID.MushroomPlants ||
                type == TileID.OasisPlants ||
                type == TileID.Cattail ||
                type == TileID.LilyPad ||
                type == TileID.Vines ||
                type == TileID.JungleVines ||
                type == TileID.CrimsonVines ||
                type == TileID.CorruptVines ||
                type == TileID.HallowedVines ||
                type == TileID.GolfGrass ||
                type == TileID.GolfGrassHallowed ||
                type == TileID.Seaweed ||
                type == TileID.SeaOats ||
                type == TileID.ImmatureHerbs ||
                type == TileID.MatureHerbs ||
                type == TileID.BloomingHerbs ||
                type == TileID.DyePlants ||
                type == TileID.Sunflower ||
                type == TileID.Pumpkins ||
                type == TileID.ClayPot ||
                type == TileID.PottedPlants1 ||
                type == TileID.PottedPlants2 ||
                type == TileID.PottedLavaPlants ||
                type == TileID.PottedLavaPlantTendrils ||
                type == TileID.PottedCrystalPlants ||
                type == TileID.AbigailsFlower ||
                type == TileID.PlanterBox ||
                type == TileID.LawnFlamingo;
        }

        private static bool IsMetalLikeTile(int type)
        {
            return type == TileID.Anvils ||
                type == TileID.AdamantiteForge ||
                type == TileID.MythrilAnvil ||
                type == TileID.Hellforge ||
                type == TileID.Furnaces ||
                type == TileID.MetalBars ||
                type == TileID.Containers ||
                type == TileID.Containers2 ||
                type == TileID.Safes ||
                type == TileID.PiggyBank ||
                type == TileID.MusicBoxes ||
                type == TileID.MinecartTrack ||
                type == TileID.LunarOre ||
                type == TileID.LunarCraftingStation ||
                type == TileID.HeavyWorkBench ||
                type == TileID.TinPlating ||
                type == TileID.CopperPlating;
        }

        private static void CleanupFinishedInstances()
        {
            for (int i = ActiveSounds.Count - 1; i >= 0; i--)
            {
                if (!SoundEngine.TryGetActiveSound(ActiveSounds[i], out ActiveSound? activeSound) ||
                    !activeSound.IsPlayingOrPaused)
                {
                    ActiveSounds.RemoveAt(i);
                }
            }
        }

        private static bool IsGamepadDpadPressed()
        {
            if (DpadVirtualizationSystem.AreDpadKeysHeld())
            {
                return true;
            }

            try
            {
                TriggersSet triggers = PlayerInput.Triggers.Current;
                if (triggers?.KeyStatus is Dictionary<string, bool> keyStatus &&
                    (keyStatus.TryGetValue("DpadUp", out bool up) && up ||
                     keyStatus.TryGetValue("DpadDown", out bool down) && down ||
                     keyStatus.TryGetValue("DpadLeft", out bool left) && left ||
                     keyStatus.TryGetValue("DpadRight", out bool right) && right))
                {
                    return true;
                }

                GamePadState state = GamePad.GetState(PlayerIndex.One);
                if (!state.IsConnected)
                {
                    return false;
                }

                return state.DPad.Up == ButtonState.Pressed ||
                    state.DPad.Down == ButtonState.Pressed ||
                    state.DPad.Left == ButtonState.Pressed ||
                    state.DPad.Right == ButtonState.Pressed;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsGamepadCursorActive()
        {
            if (VirtualStickService.WasAnalogStickActiveThisFrame())
            {
                return true;
            }

            if (PlayerInput.UsingGamepad)
            {
                return true;
            }

            try
            {
                GamePadState state = GamePad.GetState(PlayerIndex.One);
                if (!state.IsConnected)
                {
                    return false;
                }

                const float thumbstickThreshold = 0.2f;
                return MathF.Abs(state.ThumbSticks.Right.X) >= thumbstickThreshold ||
                    MathF.Abs(state.ThumbSticks.Right.Y) >= thumbstickThreshold ||
                    state.DPad.Up == ButtonState.Pressed ||
                    state.DPad.Down == ButtonState.Pressed ||
                    state.DPad.Left == ButtonState.Pressed ||
                    state.DPad.Right == ButtonState.Pressed;
            }
            catch
            {
                return false;
            }
        }
    }
}
