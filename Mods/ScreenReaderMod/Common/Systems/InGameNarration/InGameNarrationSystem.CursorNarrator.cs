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
    private sealed class CursorNarrator
    {
        private const float CursorLoudnessReferenceTiles = 90f;

        private readonly CursorDescriptorService _descriptorService;
        private int _lastTileX = int.MinValue;
        private int _lastTileY = int.MinValue;
        private bool _lastSmartCursorActive;
        private bool _wasHoveringPlayer;
        private int _originTileX = int.MinValue;
        private int _originTileY = int.MinValue;
        private static SoundEffect? _cursorTone;
        private static readonly List<SoundEffectInstance> ActiveInstances = new();
        private static bool _suppressNextAnnouncement;
        private string? _lastTileAnnouncementName;
        private int _lastTileAnnouncementKey = int.MinValue;

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

            if (Main.gameMenu || Main.ingameOptionsWindow || Main.InGameUI?.CurrentState is not null || PlayerInput.UsingGamepadUI)
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
            }

            bool hoveringPlayer = IsHoveringPlayer(player, cursorWorld);
            if (smartCursorActive && hoveringPlayer)
            {
                hoveringPlayer = false;
            }

            if (!PlayerInput.UsingGamepad)
            {
                _wasHoveringPlayer = hoveringPlayer;
                return;
            }

            if (hoveringPlayer)
            {
                if (!wasHoveringPlayer || tileChanged)
                {
                    AnnouncePlayer(player);
                }

                _wasHoveringPlayer = true;
                return;
            }

            _wasHoveringPlayer = false;

            bool shouldAnnounceTile = tileChanged || wasHoveringPlayer;
            if (!shouldAnnounceTile)
            {
                return;
            }

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

            string message = string.IsNullOrWhiteSpace(coordinates) ? descriptor.Name : $"{descriptor.Name}, {coordinates}";
            AnnouncementCategory category = descriptor.Category;
            AnnounceCursorMessage(message, force: true, category: category);
        }

        private void ResetAll()
        {
            ResetCursorFeedback();
            _lastSmartCursorActive = false;
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
            _originTileX = int.MinValue;
            _originTileY = int.MinValue;
            _lastTileAnnouncementName = null;
            _lastTileAnnouncementKey = int.MinValue;
        }

        private static bool IsHoveringPlayer(Player player, Vector2 cursorWorld)
        {
            Rectangle bounds = player.getRect();
            bounds.Inflate(4, 4);
            return bounds.Contains((int)cursorWorld.X, (int)cursorWorld.Y);
        }

        private void AnnouncePlayer(Player _)
        {
            ScreenReaderService.Announce("You", force: true);
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
            Vector2 screenSpace = player.Center - Main.screenPosition;
            int centeredX = (int)MathHelper.Clamp(screenSpace.X, 0f, Main.screenWidth - 1);
            int centeredY = (int)MathHelper.Clamp(screenSpace.Y, 0f, Main.screenHeight - 1);

            Main.mouseX = centeredX;
            Main.mouseY = centeredY;
            PlayerInput.MouseX = centeredX;
            PlayerInput.MouseY = centeredY;
        }

        private void UpdateOriginFromPlayer(Player player)
        {
            Vector2 chestWorld = GetPlayerChestWorld(player);
            _originTileX = (int)(chestWorld.X / 16f);
            _originTileY = (int)(chestWorld.Y / 16f);
        }

        private static void AnnounceCursorMessage(string message, bool force, AnnouncementCategory category = AnnouncementCategory.Default)
        {
            string messageKey = NormalizeKey(message);
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

        private static Vector2 GetPlayerChestWorld(Player player)
        {
            const float chestFraction = 0.25f;
            float verticalOffset = player.height * chestFraction * player.gravDir;
            return player.Center - new Vector2(0f, verticalOffset);
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
                return "origin";
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
            float volume = loudness * Main.soundVolume;

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
            try
            {
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
