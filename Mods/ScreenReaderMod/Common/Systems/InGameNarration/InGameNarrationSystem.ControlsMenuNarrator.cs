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
    private sealed class ControlsMenuNarrator
    {
        private readonly MenuUiSelectionTracker _uiTracker = new();
        private static readonly FieldInfo? UiListField = typeof(UIManageControls).GetField("_uilist", BindingFlags.NonPublic | BindingFlags.Instance);

        private UIState? _lastState;
        private string? _lastAnnouncement;
        private bool _announcedEntry;
        private bool _wasListening;

        public void Update(bool requiresPause)
        {
            if (!requiresPause)
            {
                Reset();
                return;
            }

            if (!TryGetControlsState(out UIManageControls? maybeState))
            {
                Reset();
                return;
            }

            UIManageControls state = maybeState!;

            if (!ReferenceEquals(_lastState, state))
            {
                _lastState = state;
                _uiTracker.Reset();
                _lastAnnouncement = null;
                _announcedEntry = false;
                _wasListening = false;
                PositionCursorAtListCenter(state);
            }

            if (!_announcedEntry)
            {
                string intro = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ControlsMenu.Opened", "Controls menu.");
                ScreenReaderService.Announce(intro, force: true);
                _announcedEntry = true;
            }

            if (HandleRebindingPrompt())
            {
                return;
            }

            if (TryAnnounceHover())
            {
                return;
            }
        }

        public void Reset()
        {
            _lastState = null;
            _lastAnnouncement = null;
            _announcedEntry = false;
            _wasListening = false;
            _uiTracker.Reset();
        }

        private bool HandleRebindingPrompt()
        {
            string trigger = PlayerInput.ListeningTrigger;
            bool isListening = !string.IsNullOrEmpty(trigger);

            if (isListening && !_wasListening)
            {
                string prompt = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ControlsMenu.RebindingPrompt", "Press the key or button to assign.");
                ScreenReaderService.Announce(prompt, force: true);
            }

            _wasListening = isListening;
            return isListening;
        }

        private bool TryAnnounceHover()
        {
            if (!_uiTracker.TryGetHoverLabel(Main.InGameUI, out MenuUiLabel hover))
            {
                return false;
            }

            if (!hover.IsNew)
            {
                return true;
            }

            string normalized = NormalizeLabel(hover.Text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return true;
            }

            if (string.Equals(normalized, _lastAnnouncement, StringComparison.Ordinal))
            {
                return true;
            }

            _lastAnnouncement = normalized;
            ScreenReaderService.Announce(normalized);
            return true;
        }

        private static string NormalizeLabel(string text)
        {
            string sanitized = TextSanitizer.Clean(text);
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return string.Empty;
            }

            return sanitized;
        }

        private static bool TryGetControlsState(out UIManageControls? state)
        {
            state = Main.InGameUI?.CurrentState as UIManageControls;
            return state is not null;
        }

        private static UIList? GetControlsList(UIManageControls state)
        {
            if (UiListField?.GetValue(state) is UIList list)
            {
                return list;
            }

            return null;
        }

        private static void PositionCursorAtListCenter(UIManageControls state)
        {
            UIList? list = GetControlsList(state);
            if (list is null)
            {
                return;
            }

            CalculatedStyle dims = list.GetInnerDimensions();
            PositionCursorAtCenter(dims);
        }

        private static void PositionCursorAtCenter(CalculatedStyle dims)
        {
            float x = dims.X + (dims.Width * 0.5f);
            float y = dims.Y + (dims.Height * 0.5f);
            int clampedX = (int)MathHelper.Clamp(x, 0f, Main.screenWidth - 1);
            int clampedY = (int)MathHelper.Clamp(y, 0f, Main.screenHeight - 1);

            Main.mouseX = clampedX;
            Main.mouseY = clampedY;
            PlayerInput.MouseX = clampedX;
            PlayerInput.MouseY = clampedY;
        }

    }
}
