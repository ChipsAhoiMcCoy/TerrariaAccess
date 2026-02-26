#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.MenuNarration;  // For MenuUiSelectionTracker, MenuUiLabel
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace TerrariaAccess.Common.Systems.Settings;

/// <summary>
/// Narrates the controls/keybinding menu (UIManageControls).
/// Refactored to use SettingsNarratorBase for shared functionality.
/// </summary>
internal sealed class ControlsMenuNarrator : SettingsNarratorBase
{
    // Reflection fields for UIManageControls
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

    // State tracking
    private readonly MenuUiSelectionTracker _uiTracker = new();
    private UIState? _lastState;
    private bool _announcedEntry;
    private bool _wasListening;
    private bool? _lastOnKeyboard;
    private bool? _lastOnGameplay;

    /// <inheritdoc/>
    public override bool IsActive => TryGetControlsState(out _);

    /// <summary>
    /// Updates the controls menu narrator.
    /// </summary>
    /// <param name="requiresPause">Whether the game should be paused (ignored for controls menu).</param>
    public void Update(bool requiresPause)
    {
        // Note: requiresPause is ignored - we check if the Controls menu is open directly.
        // This allows the Controls menu to be narrated in multiplayer where pause isn't possible.
        Update();
    }

    /// <inheritdoc/>
    public override void Update()
    {
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
            LastOptionAnnouncement = null;
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
            string intro = LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.ControlsMenu.Opened", "Controls menu.");
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

        TryAnnounceHover(state);
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        _lastState = null;
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
            string prompt = LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.ControlsMenu.RebindingPrompt", "Press the key or button to assign.");
            ScreenReaderService.Announce(prompt, force: true);
        }

        if (!isListening && _wasListening)
        {
            // Binding finished; force the next hover to re-announce the updated entry.
            _uiTracker.Reset();
            LastOptionAnnouncement = null;
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
                ? LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.ControlsMenu.KeyboardBindings", "Keyboard and mouse bindings")
                : LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.ControlsMenu.GamepadBindings", "Controller bindings");

            int tabIndex = onKeyboard ? 1 : 2;
            string announcement = TextSanitizer.JoinWithComma("Selected", categoryName, $"{tabIndex} of {ControlsTabCount}");
            ScreenReaderService.Announce(announcement, force: true);
            LastOptionAnnouncement = TextSanitizer.JoinWithComma(categoryName, $"{tabIndex} of {ControlsTabCount}");
            return true;
        }

        // Check if gameplay/interface selection changed
        if (onGameplay != _lastOnGameplay)
        {
            _lastOnKeyboard = onKeyboard;
            _lastOnGameplay = onGameplay;

            string categoryName = onGameplay
                ? LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.ControlsMenu.GameplayBindings", "Gameplay controls")
                : LocalizationHelper.GetTextOrFallback("Mods.TerrariaAccess.ControlsMenu.InterfaceBindings", "Interface controls");

            int tabIndex = onGameplay ? 3 : 4;
            string announcement = TextSanitizer.JoinWithComma("Selected", categoryName, $"{tabIndex} of {ControlsTabCount}");
            ScreenReaderService.Announce(announcement, force: true);
            LastOptionAnnouncement = TextSanitizer.JoinWithComma(categoryName, $"{tabIndex} of {ControlsTabCount}");
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

        // Try controls-specific label first (gives "Action: Binding" instead of raw key text)
        string normalized = string.Empty;
        if (hover.Element is not null)
        {
            normalized = NormalizeLabel(MenuUiSelectionTracker.ResolveControlsItemLabel(hover.Element));
        }

        // Fall back to generic hover text
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = NormalizeLabel(hover.Text);
        }

        // Fall back to controls button description (tab headers)
        if (string.IsNullOrWhiteSpace(normalized) && hover.Element is not null && TryDescribeControlsButton(state, hover.Element, out string controlsLabel))
        {
            normalized = controlsLabel;
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        if (string.Equals(normalized, LastOptionAnnouncement, StringComparison.Ordinal))
        {
            return true;
        }

        LastOptionAnnouncement = normalized;
        ScreenReaderService.Announce(normalized);
        return true;
    }

    private static string NormalizeLabel(string text)
    {
        string sanitized = TextSanitizer.Clean(text);
        return string.IsNullOrWhiteSpace(sanitized) ? string.Empty : sanitized;
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
            bool isActive = IsTabActive(state, kind);
            description = isActive
                ? TextSanitizer.JoinWithComma(label, $"{tabIndex} of {ControlsTabCount}", "selected")
                : TextSanitizer.JoinWithComma(label, $"{tabIndex} of {ControlsTabCount}");
        }
        else
        {
            description = label;
        }

        return true;
    }

    private static bool IsTabActive(UIManageControls state, ControlsButtonKind kind)
    {
        bool onKeyboard = ReadBoolean(state, OnKeyboardField);
        bool onGameplay = ReadBoolean(state, OnGameplayField);

        return kind switch
        {
            ControlsButtonKind.Keyboard => onKeyboard,
            ControlsButtonKind.Gamepad => !onKeyboard,
            ControlsButtonKind.Gameplay => onGameplay,
            ControlsButtonKind.Menu => !onGameplay,
            _ => false,
        };
    }

    private static string GetButtonLocalizationKey(ControlsButtonKind kind)
    {
        return kind switch
        {
            ControlsButtonKind.Keyboard => "Mods.TerrariaAccess.ControlsMenu.KeyboardBindings",
            ControlsButtonKind.Gamepad => "Mods.TerrariaAccess.ControlsMenu.GamepadBindings",
            ControlsButtonKind.Gameplay => "Mods.TerrariaAccess.ControlsMenu.GameplayBindings",
            ControlsButtonKind.Menu => "Mods.TerrariaAccess.ControlsMenu.InterfaceBindings",
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

    private bool HandleDpadNavigation()
    {
        if (!TryGetControlsState(out UIManageControls? _))
        {
            return false;
        }

        TriggersSet justPressed = PlayerInput.Triggers.JustPressed;
        int current = UILinkPointNavigator.CurrentPoint;
        int requested = -1;

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
                requested = orderedLinks[index - 1];
            }
            else if (index == 0)
            {
                requested = GetLinkedTarget(current, link => link.Up);
                if (requested < 0)
                {
                    requested = 3001;
                }
            }
            else if (orderedLinks.Count > 0 && !IsHeaderLink(current))
            {
                // Not in list and not on a header, go to last item
                requested = orderedLinks[orderedLinks.Count - 1];
            }
            // On header going up: do nothing (no wrapping)
        }
        else if (justPressed.MenuDown)
        {
            int index = orderedLinks.IndexOf(current);
            if (index >= 0 && index < orderedLinks.Count - 1)
            {
                requested = orderedLinks[index + 1];
            }
            else if (IsHeaderLink(current))
            {
                if (orderedLinks.Count > 0)
                {
                    requested = orderedLinks[0];
                }
            }
            else if (orderedLinks.Count > 0 && index < 0)
            {
                // Not in list, go to first item
                requested = orderedLinks[0];
            }
            // At bottom of list going down: do nothing (no wrapping)
        }

        // If nothing is focused, seed focus on the first controls element
        if (requested < 0 && current < 3000 && orderedLinks.Count > 0)
        {
            requested = orderedLinks[0];
        }

        if (requested > 0 && UILinkPointNavigator.Points.TryGetValue(requested, out UILinkPoint? targetLink))
        {
            UILinkPointNavigator.ChangePoint(requested);
            MoveCursorToLink(requested);
            SoundEngine.PlaySound(SoundID.MenuTick);
            AnnounceNavigatedElement(targetLink.Position);
            return true;
        }

        return false;
    }

    private void AnnounceNavigatedElement(Vector2 position)
    {
        UIState? currentState = Main.InGameUI?.CurrentState;
        UIElement? element = currentState?.GetElementAt(position);

        if (element is null || element == currentState)
        {
            return;
        }

        // Try the controls-specific resolver first (walks up to find keybinding parent,
        // returns combined "Action Name: Binding" without prefix side-effects)
        string controlsLabel = MenuUiSelectionTracker.ResolveControlsItemLabel(element);
        string normalized = NormalizeLabel(controlsLabel);

        // Fall back to generic label resolution
        if (string.IsNullOrWhiteSpace(normalized))
        {
            string genericLabel = MenuUiSelectionTracker.ResolveLabel(element);
            normalized = NormalizeLabel(genericLabel);
        }

        // Fall back to controls button description (tab headers)
        if (string.IsNullOrWhiteSpace(normalized) &&
            TryGetControlsState(out UIManageControls? state))
        {
            TryDescribeControlsButton(state!, element, out normalized);
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (string.Equals(normalized, LastOptionAnnouncement, StringComparison.Ordinal))
        {
            return;
        }
        LastOptionAnnouncement = normalized;
        _uiTracker.Reset();
        ScreenReaderService.Announce(normalized, force: true);
    }

    private static bool IsHeaderLink(int linkId)
    {
        // Header links: 3000=back, 3001=keyboard, 3002=gamepad, 3003=profile, 3004=gameplay, 3005=ui
        return linkId >= 3000 && linkId <= 3005;
    }

    private static List<int> GetPositionOrderedControlsLinks()
    {
        int minId = 3006;
        int maxId = UILinkPointNavigator.Shortcuts.FANCYUI_HIGHEST_INDEX;

        var linkPositions = new List<(int Id, Vector2 Position)>();

        for (int id = minId; id <= maxId; id++)
        {
            if (UILinkPointNavigator.Points.TryGetValue(id, out UILinkPoint? link))
            {
                linkPositions.Add((id, link.Position));
            }
        }

        const float rowTolerance = 10f;

        linkPositions.Sort((a, b) =>
        {
            float yDiff = a.Position.Y - b.Position.Y;
            if (Math.Abs(yDiff) < rowTolerance)
            {
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

    private enum ControlsButtonKind
    {
        Keyboard,
        Gamepad,
        Gameplay,
        Menu,
    }
}
