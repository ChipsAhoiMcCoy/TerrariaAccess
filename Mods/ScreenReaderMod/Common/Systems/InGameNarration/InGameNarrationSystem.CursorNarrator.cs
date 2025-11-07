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
    private sealed class CursorNarrator
    {
        private int _lastTileX = int.MinValue;
        private int _lastTileY = int.MinValue;
        private bool _lastSmartCursorActive;
        private bool _wasHoveringPlayer;
        private int _originTileX = int.MinValue;
        private int _originTileY = int.MinValue;
        private static SoundEffect? _cursorTone;
        private static readonly List<SoundEffectInstance> ActiveInstances = new();
        private static bool _suppressNextAnnouncement;
        private string? _lastSmartCursorTileAnnouncement;

        public void Update()
        {
            Player player = Main.LocalPlayer;
            if (player is null || !player.active)
            {
                ResetAll();
                return;
            }

            bool smartCursorActive = Main.SmartCursorIsUsed || Main.SmartCursorWanted;
            bool hasSmartInteract = Main.HasSmartInteractTarget;
            bool canProvideCursorFeedback = !hasSmartInteract;

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
            if (tileChanged)
            {
                PlayCursorCue(player, tileCenterWorld);

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

            if (!TileDescriptor.TryDescribe(tileX, tileY, out _, out string? name))
            {
                _lastSmartCursorTileAnnouncement = null;
                if (!smartCursorActive && !string.IsNullOrWhiteSpace(coordinates))
                {
                    AnnounceCursorMessage(coordinates, force: true);
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                _lastSmartCursorTileAnnouncement = null;
                if (!smartCursorActive && !string.IsNullOrWhiteSpace(coordinates))
                {
                    AnnounceCursorMessage(coordinates, force: true);
                }
                return;
            }

            if (smartCursorActive)
            {
                if (string.Equals(name, _lastSmartCursorTileAnnouncement, StringComparison.Ordinal))
                {
                    return;
                }

                _lastSmartCursorTileAnnouncement = name;
            }
            else
            {
                _lastSmartCursorTileAnnouncement = null;
            }

            string message = string.IsNullOrWhiteSpace(coordinates) ? name : $"{name}, {coordinates}";
            AnnounceCursorMessage(message, force: true);
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
            _lastSmartCursorTileAnnouncement = null;
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

        private static void AnnounceCursorMessage(string message, bool force)
        {
            if (HotbarNarrator.TryDequeuePendingAnnouncement(out string hotbarAnnouncement))
            {
                string combined = string.IsNullOrWhiteSpace(hotbarAnnouncement)
                    ? message
                    : $"{hotbarAnnouncement}. {message}";

                ScreenReaderService.Announce(combined, force: force);
                return;
            }

            ScreenReaderService.Announce(message, force: force);
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

        private static void PlayCursorCue(Player player, Vector2 tileCenterWorld)
        {
            CleanupFinishedInstances();

            Vector2 offset = tileCenterWorld - player.Center;
            SoundEffect tone = EnsureCursorTone();

            float pan = MathHelper.Clamp(offset.X / 480f, -1f, 1f);
            float pitch = MathHelper.Clamp(-offset.Y / 320f, -0.6f, 0.6f);
            float volume = MathHelper.Clamp(0.35f + Math.Abs(pitch) * 0.2f, 0f, 0.7f);

            SoundEffectInstance instance = tone.CreateInstance();
            instance.IsLooped = false;
            instance.Volume = volume * Main.soundVolume;
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
    }
}
