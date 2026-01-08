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
using ScreenReaderMod.Common;
using ScreenReaderMod.Common.Services;
using ScreenReaderMod.Common.Systems.GamepadEmulation;
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
        private static SoundEffect? _cursorTone;
        private static readonly List<SoundEffectInstance> ActiveInstances = new();
        private static bool _suppressNextAnnouncement;
        private string? _lastTileAnnouncementName;
        private int _lastTileAnnouncementKey = int.MinValue;
        private TileContentSignature _lastTileContentSignature;
        private TileStateSignature _lastTileStateSignature;

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
                IsToggleableTile == other.IsToggleableTile;

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
                PlayerInput.UsingGamepadUI || AccessibleWireColorMenu.Instance.IsOpen)
            {
                ResetCursorFeedback();
                return;
            }

            bool smartCursorActive = Main.SmartCursorIsUsed || Main.SmartCursorWanted;
            bool hasSmartInteract = Main.HasSmartInteractTarget;
            bool canProvideCursorFeedback = !hasSmartInteract || PlayerInput.UsingGamepad;

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

            if (ConsumeSuppressionFlag())
            {
                _wasHoveringPlayer = IsHoveringPlayer(player, cursorWorld);
                return;
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

            if (!PlayerInput.UsingGamepad && !DpadVirtualizationSystem.AreDpadKeysHeld() && !contentChanged && !stateOnlyChanged && !_justTransitionedToTileByTile)
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
                    currentStateSignature.IsToggleableTile, currentStateSignature.IsToggleableOn);

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

            if (!smartCursorActive && PlayerInput.UsingGamepad && !IsGamepadDpadPressed() &&
                string.Equals(descriptor.Name, "Empty", StringComparison.OrdinalIgnoreCase) && !suppressedWall)
            {
                _lastTileAnnouncementName = null;
                _lastTileAnnouncementKey = int.MinValue;
                return;
            }

            int announcementKey = CursorDescriptorService.ResolveAnnouncementKey(descriptor.TileType);

            bool suppressRepeats = smartCursorActive || (PlayerInput.UsingGamepad && !IsGamepadDpadPressed());
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

            // Check for pending "Tile by Tile" prefix from SmartCursorNarrator
            if (SmartCursorNarrator.TryDequeuePendingPrefix(out string smartCursorPrefix))
            {
                announcement = $"{smartCursorPrefix}. {announcement}";
            }

            ScreenReaderService.Announce(announcement, force: true);
        }

        public static void SuppressNextAnnouncement()
        {
            _suppressNextAnnouncement = true;
        }

        private static bool ConsumeSuppressionFlag()
        {
            if (!_suppressNextAnnouncement)
            {
                return false;
            }

            _suppressNextAnnouncement = false;
            return true;
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
            string messageKey = NormalizeKey(message);

            // Check for pending "Tile by Tile" prefix from SmartCursorNarrator
            if (SmartCursorNarrator.TryDequeuePendingPrefix(out string smartCursorPrefix))
            {
                message = $"{smartCursorPrefix}. {message}";
            }

            if (HotbarNarrator.TryDequeuePendingAnnouncement(out string hotbarAnnouncement, out string? hotbarKey))
            {
                string combined = string.IsNullOrWhiteSpace(hotbarAnnouncement)
                    ? message
                    : $"{hotbarAnnouncement}. {message}";

                NarrationInstrumentationContext.SetPendingKey(hotbarKey ?? $"cursor:{messageKey}");
                ScreenReaderService.Announce(combined, force: force, category: category);
                return;
            }

            NarrationInstrumentationContext.SetPendingKey($"cursor:{messageKey}");
            ScreenReaderService.Announce(message, force: force, category: category);
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
            // Check if smart cursor tile sounds are enabled in config
            if (!(ScreenReaderModConfig.Instance?.SmartCursorTileSounds ?? true))
            {
                return;
            }

            CleanupFinishedInstances();

            Vector2 offset = tileCenterWorld - player.Center;
            SoundEffect tone = EnsureCursorTone();

            float pan = MathHelper.Clamp(offset.X / 480f, -1f, 1f);
            float pitch = MathHelper.Clamp(-offset.Y / 320f, -0.6f, 0.6f);
            float baseVolume = MathHelper.Clamp(0.35f + Math.Abs(pitch) * 0.2f, 0f, 0.7f);
            if (!hasTile)
            {
                baseVolume *= 0.5f;
            }
            float distanceTiles = offset.Length() / 16f;
            float loudness = SoundLoudnessUtility.ApplyDistanceFalloff(
                baseVolume,
                distanceTiles,
                CursorLoudnessReferenceTiles,
                minFactor: 0.4f);
            float volume = loudness * Main.soundVolume * AudioVolumeDefaults.WorldCueVolumeScale;

            SoundEffectInstance instance = tone.CreateInstance();
            instance.IsLooped = false;
            instance.Volume = volume;
            instance.Pitch = pitch;
            instance.Pan = pan;
            instance.Play();
            ActiveInstances.Add(instance);
        }

        public static void DisposeStaticResources()
        {
            foreach (SoundEffectInstance instance in ActiveInstances)
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
            }

            ActiveInstances.Clear();

            if (_cursorTone is not null)
            {
                _cursorTone.Dispose();
                _cursorTone = null;
            }
        }

        private static SoundEffect EnsureCursorTone()
        {
            if (_cursorTone is null || _cursorTone.IsDisposed)
            {
                _cursorTone?.Dispose();
                _cursorTone = CreateCursorTone();
            }

            return _cursorTone;
        }

        private static SoundEffect CreateCursorTone()
        {
            const int sampleRate = 44100;
            const float durationSeconds = 0.09f;
            const float frequency = 880f;
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
            for (int i = ActiveInstances.Count - 1; i >= 0; i--)
            {
                SoundEffectInstance instance = ActiveInstances[i];
                if (instance.State == SoundState.Stopped)
                {
                    instance.Dispose();
                    ActiveInstances.RemoveAt(i);
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

                if (!PlayerInput.UsingGamepad)
                {
                    return false;
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
    }
}
