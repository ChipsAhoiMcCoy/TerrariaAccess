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
using Microsoft.Xna.Framework.Input;
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
        private const float CursorLoudnessReferenceTiles = 90f;

        private readonly CursorDescriptorService _descriptorService;
        private int _lastTileX = int.MinValue;
        private int _lastTileY = int.MinValue;
        private bool _lastSmartCursorActive;
        private bool _justTransitionedToTileByTile;
        private bool _wasHoveringPlayer;
        private PlayerBodyPart? _lastHoveredBodyPart;
        private int _originTileX = int.MinValue;
        private int _originTileY = int.MinValue;
        private static readonly List<(SoundEffect effect, SoundEffectInstance instance)> ActiveSounds = new();
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

            bool smartCursorActive = Main.SmartCursorIsUsed || Main.SmartCursorWanted;
            bool gamepadCursorActive = IsGamepadCursorActive();
            bool hasSmartInteract = Main.HasSmartInteractTarget;
            bool canProvideCursorFeedback = !hasSmartInteract || gamepadCursorActive;

            if (_lastSmartCursorActive && !smartCursorActive && canProvideCursorFeedback)
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
            bool hasTile = TryGetTilePresence(tileX, tileY);
            if (tileChanged)
            {
                PlayCursorCue(player, tileCenterWorld, hasTile);

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
                ? CursorDescriptorService.ResolveAnnouncementKey(descriptor.TileType, Main.tile[tileX, tileY])
                : CursorDescriptorService.ResolveAnnouncementKey(descriptor.TileType);

            // Skip repeat suppression when holding an axe so the player hears each tree announced
            bool isHoldingAxe = (player?.HeldItem?.axe ?? 0) > 0;
            bool suppressRepeats = !isHoldingAxe && (smartCursorActive || (gamepadCursorActive && !IsGamepadDpadPressed()));
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

        private static bool TryGetTilePresence(int tileX, int tileY)
        {
            if (!WorldGen.InWorld(tileX, tileY, 1))
            {
                return false;
            }

            Tile tile = Main.tile[tileX, tileY];
            return tile.HasTile;
        }

        private static void PlayCursorCue(Player player, Vector2 tileCenterWorld, bool hasTile)
        {
            // Check if cursor tile sounds are enabled in config
            if (!(TerrariaAccessConfig.Instance?.CursorTileSounds ?? true))
            {
                return;
            }

            CleanupFinishedInstances();

            // Calculate frequency based on screen position:
            // - Top of screen: 1000 Hz
            // - Middle of screen: 440 Hz
            // - Bottom of screen: 200 Hz
            // This automatically adapts to any resolution or zoom level
            Vector2 screenPos = tileCenterWorld - Main.screenPosition;
            float normalizedScreenY = MathHelper.Clamp(screenPos.Y / Main.screenHeight, 0f, 1f);
            float frequency;
            if (normalizedScreenY <= 0.5f)
            {
                // Top half of screen: interpolate 1000 Hz down to 440 Hz
                float t = normalizedScreenY * 2f; // 0 at top, 1 at middle
                frequency = MathHelper.Lerp(1000f, 440f, t);
            }
            else
            {
                // Bottom half of screen: interpolate 440 Hz down to 200 Hz
                float t = (normalizedScreenY - 0.5f) * 2f; // 0 at middle, 1 at bottom
                frequency = MathHelper.Lerp(440f, 200f, t);
            }

            SoundEffect tone = CreateCursorTone(frequency);

            // Use Terraria-aligned pan calculation; volume uses custom falloff for cursor feedback
            SpatialAudioPanner.SpatialAudioSample sample = SpatialAudioPanner.Compute(
                player.Center,
                tileCenterWorld);

            float baseVolume = 0.45f;
            float loudness = SoundLoudnessUtility.ApplyDistanceFalloff(
                baseVolume,
                sample.DistanceTiles,
                CursorLoudnessReferenceTiles,
                minFactor: 0.4f);
            float configVolume = TerrariaAccessConfig.Instance?.CursorVolume ?? 1f;
            float volume = loudness * configVolume * Main.soundVolume * AudioVolumeDefaults.WorldCueVolumeScale;

            SoundEffectInstance instance = tone.CreateInstance();
            instance.IsLooped = false;
            instance.Volume = volume;
            instance.Pitch = 0f;
            instance.Pan = sample.Pan;
            instance.Play();
            ActiveSounds.Add((tone, instance));
        }

        public static void DisposeStaticResources()
        {
            foreach (var (effect, instance) in ActiveSounds)
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
                effect.Dispose();
            }

            ActiveSounds.Clear();
        }

        private static SoundEffect CreateCursorTone(float frequency)
        {
            const int sampleRate = 44100;
            const float durationSeconds = 0.045f;
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
            for (int i = ActiveSounds.Count - 1; i >= 0; i--)
            {
                var (effect, instance) = ActiveSounds[i];
                if (instance.State == SoundState.Stopped)
                {
                    instance.Dispose();
                    effect.Dispose();
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
