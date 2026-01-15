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

        private const int ControlsTabCount = 4;

        private UIState? _lastState;
        private string? _lastAnnouncement;
        private bool _announcedEntry;
        private bool _wasListening;
        private bool? _lastOnKeyboard;
        private bool? _lastOnGameplay;

        public void Update(bool requiresPause)
        {
            // Note: requiresPause is ignored - we check if the Controls menu is open directly.
            // This allows the Controls menu to be narrated in multiplayer where pause isn't possible.
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

            if (TryAnnounceCategorySelection(state))
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
            _lastOnKeyboard = null;
            _lastOnGameplay = null;
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

        private bool TryAnnounceCategorySelection(UIManageControls state)
        {
            bool onKeyboard = ReadBoolean(state, OnKeyboardField);
            bool onGameplay = ReadBoolean(state, OnGameplayField);

            // First time seeing this state - just store values without announcing
            if (_lastOnKeyboard is null || _lastOnGameplay is null)
            {
                _lastOnKeyboard = onKeyboard;
                _lastOnGameplay = onGameplay;
                return false;
            }

            // Check if keyboard/gamepad selection changed
            if (onKeyboard != _lastOnKeyboard)
            {
                _lastOnKeyboard = onKeyboard;
                _lastOnGameplay = onGameplay;

                string categoryName = onKeyboard
                    ? LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ControlsMenu.KeyboardBindings", "Keyboard and mouse bindings")
                    : LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ControlsMenu.GamepadBindings", "Controller bindings");

                int tabIndex = onKeyboard ? 1 : 2;
                string announcement = TextSanitizer.JoinWithComma("Selected", categoryName, $"{tabIndex} of {ControlsTabCount}");
                ScreenReaderService.Announce(announcement, force: true);
                // Set _lastAnnouncement to the hover version to prevent duplicate
                _lastAnnouncement = TextSanitizer.JoinWithComma(categoryName, $"{tabIndex} of {ControlsTabCount}");
                return true;
            }

            // Check if gameplay/interface selection changed
            if (onGameplay != _lastOnGameplay)
            {
                _lastOnKeyboard = onKeyboard;
                _lastOnGameplay = onGameplay;

                string categoryName = onGameplay
                    ? LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ControlsMenu.GameplayBindings", "Gameplay controls")
                    : LocalizationHelper.GetTextOrFallback("Mods.ScreenReaderMod.ControlsMenu.InterfaceBindings", "Interface controls");

                int tabIndex = onGameplay ? 3 : 4;
                string announcement = TextSanitizer.JoinWithComma("Selected", categoryName, $"{tabIndex} of {ControlsTabCount}");
                ScreenReaderService.Announce(announcement, force: true);
                // Set _lastAnnouncement to the hover version to prevent duplicate
                _lastAnnouncement = TextSanitizer.JoinWithComma(categoryName, $"{tabIndex} of {ControlsTabCount}");
                return true;
            }

            return false;
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

            int tabIndex = GetTabIndex(kind);
            if (tabIndex > 0)
            {
                description = TextSanitizer.JoinWithComma(label, $"{tabIndex} of {ControlsTabCount}");
            }
            else
            {
                description = label;
            }

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
            if (!TryGetControlsState(out UIManageControls? _))
            {
                return false;
            }

            TriggersSet justPressed = PlayerInput.Triggers.JustPressed;
            int current = UILinkPointNavigator.CurrentPoint;
            int requested = -1;

            // Get all controls links sorted by visual position (top-to-bottom, left-to-right)
            List<int> orderedLinks = GetPositionOrderedControlsLinks();

            // Handle left/right navigation for header links
            if (IsHeaderLink(current) && (justPressed.MenuLeft || justPressed.MenuRight))
            {
                int neighbor = justPressed.MenuLeft
                    ? GetLinkedTarget(current, link => link.Left)
                    : GetLinkedTarget(current, link => link.Right);

                if (neighbor > 0)
                {
                    requested = neighbor;
                }
            }

            // Handle up/down navigation through the ordered list
            if (justPressed.MenuUp)
            {
                int index = orderedLinks.IndexOf(current);
                if (index > 0)
                {
                    // Move to previous item in list
                    requested = orderedLinks[index - 1];
                }
                else if (index == 0)
                {
                    // At top of list - go back to header tabs
                    requested = GetLinkedTarget(current, link => link.Up);
                    if (requested < 0)
                    {
                        // Fallback to first header if no up link
                        requested = 3001;
                    }
                }
                else if (IsHeaderLink(current))
                {
                    // From header going up, wrap to bottom of list
                    if (orderedLinks.Count > 0)
                    {
                        requested = orderedLinks[orderedLinks.Count - 1];
                    }
                }
                else if (orderedLinks.Count > 0)
                {
                    // Not in list, go to last item
                    requested = orderedLinks[orderedLinks.Count - 1];
                }
            }
            else if (justPressed.MenuDown)
            {
                int index = orderedLinks.IndexOf(current);
                if (index >= 0 && index < orderedLinks.Count - 1)
                {
                    // Move to next item in list
                    requested = orderedLinks[index + 1];
                }
                else if (index == orderedLinks.Count - 1)
                {
                    // At bottom of list - wrap to header tabs
                    requested = 3001;
                }
                else if (IsHeaderLink(current))
                {
                    // From header going down, go to first list item
                    if (orderedLinks.Count > 0)
                    {
                        requested = orderedLinks[0];
                    }
                }
                else if (orderedLinks.Count > 0)
                {
                    // Not in list, go to first item
                    requested = orderedLinks[0];
                }
            }

            // If nothing is focused, seed focus on the first controls element
            if (requested < 0 && current < 3000 && orderedLinks.Count > 0)
            {
                requested = orderedLinks[0];
            }

            if (requested > 0 && UILinkPointNavigator.Points.TryGetValue(requested, out UILinkPoint? _))
            {
                UILinkPointNavigator.ChangePoint(requested);
                MoveCursorToLink(requested);
                SoundEngine.PlaySound(SoundID.MenuTick);
                return true;
            }

            return false;
        }

        private static bool IsHeaderLink(int linkId)
        {
            // Header links: 3000=back, 3001=keyboard, 3002=gamepad, 3003=profile, 3004=gameplay, 3005=ui
            return linkId >= 3000 && linkId <= 3005;
        }

        private static List<int> GetPositionOrderedControlsLinks()
        {
            int minId = 3006; // First content link after headers
            int maxId = UILinkPointNavigator.Shortcuts.FANCYUI_HIGHEST_INDEX;

            var linkPositions = new List<(int Id, Vector2 Position)>();

            for (int id = minId; id <= maxId; id++)
            {
                if (UILinkPointNavigator.Points.TryGetValue(id, out UILinkPoint? link))
                {
                    linkPositions.Add((id, link.Position));
                }
            }

            // Sort by Y position first (top to bottom), then by X position (left to right)
            // Using a small tolerance for Y to handle items on the same row
            const float rowTolerance = 10f;

            linkPositions.Sort((a, b) =>
            {
                float yDiff = a.Position.Y - b.Position.Y;
                if (Math.Abs(yDiff) < rowTolerance)
                {
                    // Same row, sort by X
                    return a.Position.X.CompareTo(b.Position.X);
                }
                return yDiff.CompareTo(0f);
            });

            return linkPositions.Select(lp => lp.Id).ToList();
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

        private static void MoveCursorToLink(int linkId)
        {
            if (!UILinkPointNavigator.Points.TryGetValue(linkId, out UILinkPoint? link))
            {
                return;
            }

            int clampedX = (int)MathHelper.Clamp(link.Position.X, 0f, Main.screenWidth - 1);
            int clampedY = (int)MathHelper.Clamp(link.Position.Y, 0f, Main.screenHeight - 1);

            Main.mouseX = clampedX;
            Main.mouseY = clampedY;
            PlayerInput.MouseX = clampedX;
            PlayerInput.MouseY = clampedY;
        }

    }
}
