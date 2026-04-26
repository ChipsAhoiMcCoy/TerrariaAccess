#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Gamepad;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.MenuNarration;
using TerrariaAccess.Common.Utilities;

namespace TerrariaAccess.Common.Systems;

/// <summary>
/// Lets keyboard users edit the world creation name field in-place with Tab,
/// avoiding the virtual keyboard screen and UI navigation key conflicts.
/// </summary>
public sealed class WorldCreationNameInputSystem : ModSystem
{
    private const int MaxNameLength = 27;

    private bool _isEditingName;
    private bool _tabWasPressed;
    private string _editingText = string.Empty;
    private string _originalText = string.Empty;
    private UIState? _editingState;
    private UIElement? _nameButton;

    internal static bool IsNameEntryActive { get; private set; }

    public override void Unload()
    {
        ResetState();
    }

    public override void PostUpdateInput()
    {
        if (Main.dedServ)
        {
            return;
        }

        UIState? state = TryGetWorldCreationState();
        if (state is null)
        {
            ResetState();
            _tabWasPressed = false;
            return;
        }

        bool tabPressed = Main.keyState.IsKeyDown(Keys.Tab);
        bool tabJustPressed = tabPressed && !_tabWasPressed;
        _tabWasPressed = tabPressed;

        if (_isEditingName && !ReferenceEquals(state, _editingState))
        {
            CommitName(save: true, announce: false);
        }

        if (tabJustPressed)
        {
            if (_isEditingName)
            {
                CommitName(save: true, announce: true);
            }
            else if (IsWorldNameFocused())
            {
                BeginNameEntry(state);
            }

            return;
        }

        if (_isEditingName)
        {
            UpdateNameEntry(state);
        }
    }

    private void BeginNameEntry(UIState state)
    {
        if (!TryGetWorldCreationControls(state, out UIElement? nameButton))
        {
            return;
        }

        _editingState = state;
        _nameButton = nameButton;
        _originalText = GetCurrentWorldName(state, nameButton);
        _editingText = _originalText;
        _isEditingName = true;
        IsNameEntryActive = true;

        ConfigureTextInput(nameButton);
        BlockTabForCurrentFrame();

        SoundEngine.PlaySound(SoundID.MenuOpen);
        ScreenReaderService.Announce(
            LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.WorldCreationNameInput.TextEditingEnabled",
                BuildNameEditingAnnouncement(_editingText)),
            force: true);
    }

    private void UpdateNameEntry(UIState state)
    {
        if (!TryGetWorldCreationControls(state, out UIElement? nameButton))
        {
            CommitName(save: true, announce: false);
            return;
        }

        _editingState = state;
        _nameButton = nameButton;
        ConfigureTextInput(nameButton);
        SuppressNavigationTriggers();

        Main.instance.HandleIME();
        string previousText = _editingText;
        string updatedText = Main.GetInputText(_editingText);
        if (updatedText.Length > MaxNameLength)
        {
            updatedText = updatedText[..MaxNameLength];
        }

        if (Main.inputTextEscape)
        {
            Main.inputTextEscape = false;
            _editingText = _originalText;
            SetWorldName(state, nameButton, _editingText);
            CommitName(save: false, announce: true);
            return;
        }

        if (Main.inputTextEnter)
        {
            Main.inputTextEnter = false;
            _editingText = updatedText;
            SetWorldName(state, nameButton, _editingText);
            CommitName(save: true, announce: true);
            return;
        }

        if (!string.Equals(updatedText, previousText, StringComparison.Ordinal))
        {
            _editingText = updatedText;
            SetWorldName(state, nameButton, _editingText);
            SoundEngine.PlaySound(SoundID.MenuTick);
        }
    }

    private void CommitName(bool save, bool announce)
    {
        if (_editingState is not null &&
            TryGetWorldCreationControls(_editingState, out UIElement? nameButton))
        {
            string finalText = save ? _editingText.Trim() : _originalText;
            SetWorldName(_editingState, nameButton, finalText);
            _editingText = finalText;
        }

        string announcedName = string.IsNullOrWhiteSpace(_editingText)
            ? LocalizationHelper.GetTextOrFallback("UI.WorldCreationNameEmpty", "Enter world name")
            : TextSanitizer.Clean(_editingText);

        ClearTextInput();
        _isEditingName = false;
        IsNameEntryActive = false;
        _editingState = null;
        _nameButton = null;
        _originalText = string.Empty;
        _editingText = string.Empty;
        BlockTabForCurrentFrame();

        if (!announce)
        {
            return;
        }

        SoundEngine.PlaySound(save ? SoundID.MenuClose : SoundID.MenuTick);
        string fallback = save
            ? $"World name saved. {announcedName}."
            : $"World name edit canceled. {announcedName}.";
        string key = save
            ? "Mods.TerrariaAccess.WorldCreationNameInput.NameSaved"
            : "Mods.TerrariaAccess.WorldCreationNameInput.NameCanceled";
        ScreenReaderService.Announce(LocalizationHelper.GetTextOrFallback(key, fallback), force: true);
    }

    private static UIState? TryGetWorldCreationState()
    {
        UIState? state = Main.MenuUI?.CurrentState;
        Type? worldCreationType = ReflectionCache.UIWorldCreation.Type;
        if (state is null || worldCreationType is null)
        {
            return null;
        }

        return worldCreationType.IsAssignableFrom(state.GetType()) ? state : null;
    }

    private static bool TryGetWorldCreationControls(
        UIState state,
        [NotNullWhen(true)] out UIElement? nameButton)
    {
        nameButton = null;

        try
        {
            nameButton = ReflectionCache.UIWorldCreation.NamePlate?.GetValue(state) as UIElement;
            return nameButton is not null;
        }
        catch (Exception ex)
        {
            TerrariaAccess.Instance?.Logger.Warn($"[WorldCreationNameInput] Failed to access name controls: {ex.Message}");
            return false;
        }
    }

    private static bool IsWorldNameFocused()
    {
        return UILinkPointNavigator.CurrentPoint == MenuUiSelectionTracker.WcLinkName;
    }

    private static string GetCurrentWorldName(UIState state, UIElement nameButton)
    {
        try
        {
            if (ReflectionCache.UIWorldCreation.WorldName?.GetValue(state) is string worldName)
            {
                return worldName;
            }
        }
        catch
        {
            // Fall through to the name plate contents.
        }

        try
        {
            return ReflectionCache.UICharacterNameButton.ActualContents?.GetValue(nameButton) as string ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void SetWorldName(UIState state, UIElement nameButton, string name)
    {
        try
        {
            ReflectionCache.UIWorldCreation.WorldName?.SetValue(state, name);
        }
        catch (Exception ex)
        {
            TerrariaAccess.Instance?.Logger.Warn($"[WorldCreationNameInput] Failed to update world name field: {ex.Message}");
        }

        try
        {
            if (ReflectionCache.UIWorldCreation.UpdateInputFields is not null)
            {
                ReflectionCache.UIWorldCreation.UpdateInputFields.Invoke(state, Array.Empty<object?>());
                return;
            }
        }
        catch (Exception ex)
        {
            TerrariaAccess.Instance?.Logger.Warn($"[WorldCreationNameInput] Failed to refresh input fields: {ex.Message}");
        }

        try
        {
            ReflectionCache.UICharacterNameButton.SetContents?.Invoke(nameButton, new object?[] { name });
            nameButton.Recalculate();
            ReflectionCache.UICharacterNameButton.TrimDisplayIfOverElementDimensions?.Invoke(nameButton, new object?[] { MaxNameLength });
            nameButton.Recalculate();
        }
        catch (Exception ex)
        {
            TerrariaAccess.Instance?.Logger.Warn($"[WorldCreationNameInput] Failed to update name button: {ex.Message}");
        }
    }

    private static void ConfigureTextInput(UIElement nameButton)
    {
        PlayerInput.CurrentInputMode = InputMode.Keyboard;
        PlayerInput.WritingText = true;
        Main.CurrentInputTextTakerOverride = nameButton;
        Main.drawingPlayerChat = false;
        Main.chatRelease = false;
        SuppressNavigationTriggers();
    }

    private void ClearTextInput()
    {
        if (_nameButton is not null && ReferenceEquals(Main.CurrentInputTextTakerOverride, _nameButton))
        {
            Main.CurrentInputTextTakerOverride = null;
        }

        PlayerInput.WritingText = false;
        Main.inputTextEnter = false;
        Main.inputTextEscape = false;
        Main.chatRelease = false;
    }

    private static void BlockTabForCurrentFrame()
    {
        Main.clrInput();
        Main.blockKey = Keys.Tab.ToString();
        SuppressNavigationTriggers();
    }

    private static void SuppressNavigationTriggers()
    {
        TriggersSet current = PlayerInput.Triggers.Current;
        TriggersSet justPressed = PlayerInput.Triggers.JustPressed;

        ClearTrigger(current, justPressed, TriggerNames.Up);
        ClearTrigger(current, justPressed, TriggerNames.Down);
        ClearTrigger(current, justPressed, TriggerNames.Left);
        ClearTrigger(current, justPressed, TriggerNames.Right);
        ClearTrigger(current, justPressed, TriggerNames.MenuUp);
        ClearTrigger(current, justPressed, TriggerNames.MenuDown);
        ClearTrigger(current, justPressed, TriggerNames.MenuLeft);
        ClearTrigger(current, justPressed, TriggerNames.MenuRight);
        ClearTrigger(current, justPressed, TriggerNames.MouseLeft);
        ClearTrigger(current, justPressed, TriggerNames.MouseRight);

        Main.mouseLeft = false;
        Main.mouseLeftRelease = false;
        Main.mouseRight = false;
        Main.mouseRightRelease = false;
        PlayerInput.GamepadThumbstickLeft = Vector2.Zero;
    }

    private static void ClearTrigger(TriggersSet current, TriggersSet justPressed, string triggerName)
    {
        current.KeyStatus[triggerName] = false;
        justPressed.KeyStatus[triggerName] = false;
    }

    private void ResetState()
    {
        ClearTextInput();
        _isEditingName = false;
        IsNameEntryActive = false;
        _editingState = null;
        _nameButton = null;
        _editingText = string.Empty;
        _originalText = string.Empty;
    }

    private static string BuildNameEditingAnnouncement(string currentName)
    {
        string value = string.IsNullOrWhiteSpace(currentName)
            ? LocalizationHelper.GetTextOrFallback("UI.WorldCreationNameEmpty", "Enter world name")
            : TextSanitizer.Clean(currentName);

        return $"World name editing. Current name: {value}. Type the world name. Press Tab to save.";
    }
}
