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
        private static readonly FieldInfo? ButtonKeyboardField = typeof(UIManageControls).GetField("_buttonKeyboard", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? ButtonGamepadField = typeof(UIManageControls).GetField("_buttonGamepad", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? ButtonGameplayField = typeof(UIManageControls).GetField("_buttonVs1", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? ButtonMenuField = typeof(UIManageControls).GetField("_buttonVs2", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? ButtonBorderKeyboardField = typeof(UIManageControls).GetField("_buttonBorder1", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? ButtonBorderGamepadField = typeof(UIManageControls).GetField("_buttonBorder2", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? ButtonBorderGameplayField = typeof(UIManageControls).GetField("_buttonBorderVs1", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? ButtonBorderMenuField = typeof(UIManageControls).GetField("_buttonBorderVs2", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? OnKeyboardField = typeof(UIManageControls).GetField("OnKeyboard", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        private static readonly FieldInfo? OnGameplayField = typeof(UIManageControls).GetField("OnGameplay", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

        private static readonly (FieldInfo? Field, ControlsButtonKind Kind)[] ControlsButtonDescriptors = new[]
        {
            (ButtonKeyboardField, ControlsButtonKind.Keyboard),
            (ButtonBorderKeyboardField, ControlsButtonKind.Keyboard),
            (ButtonGamepadField, ControlsButtonKind.Gamepad),
            (ButtonBorderGamepadField, ControlsButtonKind.Gamepad),
            (ButtonGameplayField, ControlsButtonKind.Gameplay),
            (ButtonBorderGameplayField, ControlsButtonKind.Gameplay),
            (ButtonMenuField, ControlsButtonKind.Menu),
            (ButtonBorderMenuField, ControlsButtonKind.Menu),
        };

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

            if (HandleDpadNavigation())
            {
                return;
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

            if (TryAnnounceHover(state))
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

            if (!isListening && _wasListening)
            {
                // Binding finished; force the next hover to re-announce the updated entry.
                _uiTracker.Reset();
                _lastAnnouncement = null;
            }

            _wasListening = isListening;
            return isListening;
        }

        private bool TryAnnounceHover(UIManageControls state)
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
            if (string.IsNullOrWhiteSpace(normalized) && hover.Element is not null && TryDescribeControlsButton(state, hover.Element, out string controlsLabel))
            {
                normalized = controlsLabel;
            }
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

        private static bool TryDescribeControlsButton(UIManageControls state, UIElement? hovered, out string description)
        {
            description = string.Empty;
            if (hovered is null)
            {
                return false;
            }

            foreach ((FieldInfo? field, ControlsButtonKind kind) in ControlsButtonDescriptors)
            {
                if (TryMatchControlsButton(state, hovered, field, kind, out description))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryMatchControlsButton(UIManageControls state, UIElement hovered, FieldInfo? field, ControlsButtonKind kind, out string description)
        {
            description = string.Empty;
            if (field?.GetValue(state) is not UIElement target)
            {
                return false;
            }

            if (!IsElementWithin(target, hovered))
            {
                return false;
            }

            string labelKey = GetButtonLocalizationKey(kind);
            string fallback = GetButtonFallback(kind);
            string label = LocalizationHelper.GetTextOrFallback(labelKey, fallback);

            if (IsButtonSelected(state, kind))
            {
                string suffix = LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ControlsMenu.SelectedSuffix", "Selected");
                if (!string.IsNullOrWhiteSpace(suffix))
                {
                    label = $"{label} ({suffix})";
                }
            }

            int tabIndex = GetTabIndex(kind);
            if (tabIndex > 0)
            {
                label = $"{label} (Tab {tabIndex})";
            }

            description = label;
            return true;
        }

        private static string GetButtonLocalizationKey(ControlsButtonKind kind)
        {
            return kind switch
            {
                ControlsButtonKind.Keyboard => "Mods.ScreenReaderMod.ControlsMenu.KeyboardBindings",
                ControlsButtonKind.Gamepad => "Mods.ScreenReaderMod.ControlsMenu.GamepadBindings",
                ControlsButtonKind.Gameplay => "Mods.ScreenReaderMod.ControlsMenu.GameplayBindings",
                ControlsButtonKind.Menu => "Mods.ScreenReaderMod.ControlsMenu.InterfaceBindings",
                _ => string.Empty,
            };
        }

        private static string GetButtonFallback(ControlsButtonKind kind)
        {
            return kind switch
            {
                ControlsButtonKind.Keyboard => "Keyboard and mouse bindings",
                ControlsButtonKind.Gamepad => "Controller bindings",
                ControlsButtonKind.Gameplay => "Gameplay controls",
                ControlsButtonKind.Menu => "Interface controls",
                _ => string.Empty,
            };
        }

        private static bool IsButtonSelected(UIManageControls state, ControlsButtonKind kind)
        {
            bool keyboardSelected = ReadBoolean(state, OnKeyboardField);
            bool gameplaySelected = ReadBoolean(state, OnGameplayField);

            return kind switch
            {
                ControlsButtonKind.Keyboard => keyboardSelected,
                ControlsButtonKind.Gamepad => !keyboardSelected,
                ControlsButtonKind.Gameplay => gameplaySelected,
                ControlsButtonKind.Menu => !gameplaySelected,
                _ => false,
            };
        }

        private static bool ReadBoolean(UIManageControls state, FieldInfo? field)
        {
            if (field?.GetValue(state) is bool value)
            {
                return value;
            }

            return false;
        }

        private static bool IsElementWithin(UIElement target, UIElement candidate)
        {
            UIElement? current = candidate;
            while (current is not null)
            {
                if (ReferenceEquals(current, target))
                {
                    return true;
                }

                current = current.Parent;
            }

            return false;
        }

        private enum ControlsButtonKind
        {
            Keyboard,
            Gamepad,
            Gameplay,
            Menu,
        }

        private static int GetTabIndex(ControlsButtonKind kind)
        {
            return kind switch
            {
                ControlsButtonKind.Keyboard => 1,
                ControlsButtonKind.Gamepad => 2,
                ControlsButtonKind.Gameplay => 3,
                ControlsButtonKind.Menu => 4,
                _ => -1,
            };
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

        private static bool HandleDpadNavigation()
        {
            if (!TryGetControlsState(out UIManageControls? state))
            {
                return false;
            }

            TriggersSet justPressed = PlayerInput.Triggers.JustPressed;
            int requested = -1;
            int current = UILinkPointNavigator.CurrentPoint;

            if (justPressed.MenuUp)
            {
                requested = GetLinkedTarget(current, link => link.Up);
            }
            else if (justPressed.MenuDown)
            {
                requested = GetLinkedTarget(current, link => link.Down);
            }
            else if (justPressed.MenuLeft)
            {
                requested = GetLinkedTarget(current, link => link.Left);
            }
            else if (justPressed.MenuRight)
            {
                requested = GetLinkedTarget(current, link => link.Right);
            }

            // If nothing is focused, seed focus on the first controls element.
            if (requested < 0 && current < 3000)
            {
                requested = 3000;
            }

            if (requested > 0 && UILinkPointNavigator.Points.TryGetValue(requested, out UILinkPoint? _))
            {
                UILinkPointNavigator.ChangePoint(requested);
                SoundEngine.PlaySound(SoundID.MenuTick);
                return true;
            }

            return false;
        }

        private static int GetLinkedTarget(int currentPoint, Func<UILinkPoint, int> selector)
        {
            if (!UILinkPointNavigator.Points.TryGetValue(currentPoint, out UILinkPoint? link))
            {
                return -1;
            }

            int target = selector(link);
            if (target >= 0 && UILinkPointNavigator.Points.ContainsKey(target))
            {
                return target;
            }

            return -1;
        }

    }
}
