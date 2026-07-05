#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace TerrariaAccess.Common.Systems;

/// <summary>
/// Lets keyboard users edit the character creation name field in-place with Tab,
/// avoiding the virtual keyboard screen and UI navigation key conflicts.
/// </summary>
public sealed class CharacterCreationNameInputSystem : ModSystem
{
    private const int MaxNameLength = 20;
    private const int CharacterNameLinkPoint = 3020;

    private bool _isEditingName;
    private bool _replaceExistingTextOnFirstInput;
    private bool _tabWasPressed;
    private bool _tabHandledUntilRelease;
    private int _lastFocusedLinkPoint = -1;
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

        UIState? state = TryGetCharacterCreationState();
        if (state is null)
        {
            ResetState();
            _tabWasPressed = false;
            _tabHandledUntilRelease = false;
            _lastFocusedLinkPoint = -1;
            return;
        }

        int focusedLinkPointBeforeInput = _lastFocusedLinkPoint;
        bool tabPressed = Main.keyState.IsKeyDown(Keys.Tab);
        bool tabJustPressed = tabPressed && !_tabWasPressed;
        _tabWasPressed = tabPressed;
        if (!tabPressed)
        {
            _tabHandledUntilRelease = false;
        }

        bool tabStartedOnCharacterName = tabJustPressed &&
            !_tabHandledUntilRelease &&
            !_isEditingName &&
            focusedLinkPointBeforeInput == CharacterNameLinkPoint;
        bool tabSavePressed = tabJustPressed && !_tabHandledUntilRelease && _isEditingName;

        if (_isEditingName && !ReferenceEquals(state, _editingState))
        {
            CommitName(save: true, announce: false);
        }

        if (tabSavePressed || tabStartedOnCharacterName)
        {
            _tabHandledUntilRelease = true;

            if (_isEditingName)
            {
                BlockTabForCurrentFrame();
                CommitName(save: true, announce: true);
            }
            else if (tabStartedOnCharacterName)
            {
                BeginNameEntry(state);
            }

            return;
        }

        if (_isEditingName)
        {
            UpdateNameEntry(state);
        }

        _lastFocusedLinkPoint = UILinkPointNavigator.CurrentPoint;
    }

    private void BeginNameEntry(UIState state)
    {
        if (!TryGetCharacterCreationControls(state, out Player? player, out UIElement? nameButton))
        {
            return;
        }

        _editingState = state;
        _nameButton = nameButton;
        _originalText = GetCurrentName(player, nameButton);
        _editingText = _originalText;
        _replaceExistingTextOnFirstInput = !string.IsNullOrEmpty(_editingText);
        _isEditingName = true;
        IsNameEntryActive = true;

        UILinkPointNavigator.ChangePoint(CharacterNameLinkPoint);
        _lastFocusedLinkPoint = CharacterNameLinkPoint;
        ConfigureTextInput(nameButton);
        BlockTabForCurrentFrame();

        global::TerrariaAccess.Common.Services.UiSoundCuePlayer.PlayOpen();
        ScreenReaderService.Announce(
            LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.CharacterCreationNameInput.TextEditingEnabled",
                BuildNameEditingAnnouncement(_editingText)),
            force: true);
    }

    private void UpdateNameEntry(UIState state)
    {
        if (!TryGetCharacterCreationControls(state, out Player? player, out UIElement? nameButton))
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
        if (_replaceExistingTextOnFirstInput)
        {
            updatedText = ApplyFirstTextInputReplacement(previousText, updatedText);
        }

        if (updatedText.Length > MaxNameLength)
        {
            updatedText = updatedText[..MaxNameLength];
        }

        if (Main.inputTextEscape)
        {
            Main.inputTextEscape = false;
            _editingText = _originalText;
            SetName(player, nameButton, _editingText);
            CommitName(save: false, announce: true);
            return;
        }

        if (Main.inputTextEnter)
        {
            Main.inputTextEnter = false;
            _editingText = updatedText;
            SetName(player, nameButton, _editingText);
            CommitName(save: true, announce: true);
            return;
        }

        if (!string.Equals(updatedText, previousText, StringComparison.Ordinal))
        {
            _replaceExistingTextOnFirstInput = false;
            _editingText = updatedText;
            SetName(player, nameButton, _editingText);
            global::TerrariaAccess.Common.Services.UiSoundCuePlayer.PlayTick();
        }
    }

    private void CommitName(bool save, bool announce)
    {
        if (_editingState is not null &&
            TryGetCharacterCreationControls(_editingState, out Player? player, out UIElement? nameButton))
        {
            string finalText = save ? _editingText.Trim() : _originalText;
            SetName(player, nameButton, finalText);
            _editingText = finalText;
        }

        string announcedName = string.IsNullOrWhiteSpace(_editingText)
            ? LocalizationHelper.GetTextOrFallback("UI.PlayerEmptyName", "Enter name")
            : TextSanitizer.Clean(_editingText);

        ClearTextInput();
        _isEditingName = false;
        IsNameEntryActive = false;
        _replaceExistingTextOnFirstInput = false;
        _editingState = null;
        _nameButton = null;
        _originalText = string.Empty;
        _editingText = string.Empty;
        BlockTabForCurrentFrame();

        if (!announce)
        {
            return;
        }

        global::TerrariaAccess.Common.Services.UiSoundCuePlayer.PlayCloseOrTick(save);
        string fallback = save
            ? $"Name saved. {announcedName}."
            : $"Name edit canceled. {announcedName}.";
        string key = save
            ? "Mods.TerrariaAccess.CharacterCreationNameInput.NameSaved"
            : "Mods.TerrariaAccess.CharacterCreationNameInput.NameCanceled";
        ScreenReaderService.Announce(LocalizationHelper.GetTextOrFallback(key, fallback), force: true);
    }

    private static UIState? TryGetCharacterCreationState()
    {
        UIState? state = Main.MenuUI?.CurrentState;
        Type? characterCreationType = ReflectionCache.UICharacterCreation.Type;
        if (state is null || characterCreationType is null)
        {
            return null;
        }

        return characterCreationType.IsAssignableFrom(state.GetType()) ? state : null;
    }

    private static bool TryGetCharacterCreationControls(
        UIState state,
        [NotNullWhen(true)] out Player? player,
        [NotNullWhen(true)] out UIElement? nameButton)
    {
        player = null;
        nameButton = null;

        try
        {
            player = ReflectionCache.UICharacterCreation.Player?.GetValue(state) as Player;
            nameButton = ReflectionCache.UICharacterCreation.CharacterName?.GetValue(state) as UIElement;
            return player is not null && nameButton is not null;
        }
        catch (Exception ex)
        {
            TerrariaAccess.Instance?.Logger.Warn($"[CharacterCreationNameInput] Failed to access name controls: {ex.Message}");
            return false;
        }
    }

    private static string GetCurrentName(Player player, UIElement nameButton)
    {
        if (!string.IsNullOrWhiteSpace(player.name))
        {
            return player.name;
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

    private string ApplyFirstTextInputReplacement(string previousText, string updatedText)
    {
        if (Main.inputTextEnter || Main.inputTextEscape)
        {
            _replaceExistingTextOnFirstInput = false;
            return updatedText;
        }

        if (updatedText.Length > previousText.Length &&
            updatedText.StartsWith(previousText, StringComparison.Ordinal))
        {
            _replaceExistingTextOnFirstInput = false;
            return updatedText[previousText.Length..];
        }

        if (!string.Equals(updatedText, previousText, StringComparison.Ordinal))
        {
            _replaceExistingTextOnFirstInput = false;
        }

        return updatedText;
    }

    private static void SetName(Player player, UIElement nameButton, string name)
    {
        player.name = name;
        try
        {
            ReflectionCache.UICharacterNameButton.SetContents?.Invoke(nameButton, new object?[] { name });
        }
        catch (Exception ex)
        {
            TerrariaAccess.Instance?.Logger.Warn($"[CharacterCreationNameInput] Failed to update name button: {ex.Message}");
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
        ClearTrigger(current, justPressed, TriggerNames.LockOn);
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
        _replaceExistingTextOnFirstInput = false;
        _tabHandledUntilRelease = false;
        _lastFocusedLinkPoint = -1;
        _editingState = null;
        _nameButton = null;
        _editingText = string.Empty;
        _originalText = string.Empty;
    }

    private static string BuildNameEditingAnnouncement(string currentName)
    {
        string value = string.IsNullOrWhiteSpace(currentName)
            ? LocalizationHelper.GetTextOrFallback("UI.PlayerEmptyName", "Enter name")
            : TextSanitizer.Clean(currentName);

        return $"Player name editing. Current name: {value}. Type the player name. Press Tab to save.";
    }
}
