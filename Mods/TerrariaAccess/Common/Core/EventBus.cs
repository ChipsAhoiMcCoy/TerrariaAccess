#nullable enable
using System;

namespace TerrariaAccess.Common.Core;

/// <summary>
/// A simple event bus for decoupled communication between systems.
/// Replaces scattered static flags (e.g., IsHandlingGamepadInput) with events
/// that systems can subscribe to without direct coupling.
/// </summary>
/// <remarks>
/// Design principles:
/// - Events are fire-and-forget; publishers don't wait for handlers
/// - Handlers should be lightweight; heavy work should be deferred
/// - Events are synchronous on the calling thread (typically main thread)
///
/// Usage example:
/// <code>
/// // Subscribing (during Load):
/// EventBus.OnGamepadInputHandled += HandleGamepadInput;
///
/// // Publishing:
/// EventBus.RaiseGamepadInputHandled(new GamepadInputEvent(sourceSystem));
///
/// // Unsubscribing (during Unload):
/// EventBus.OnGamepadInputHandled -= HandleGamepadInput;
/// </code>
///
/// Thread safety: All event operations should occur on the main game thread.
/// </remarks>
public static class EventBus
{
    #region Gamepad Input Events

    /// <summary>
    /// Raised when a system handles gamepad-style input (real or emulated).
    /// Subscribers can use this to coordinate UI navigation behavior.
    /// </summary>
    public static event Action<GamepadInputEvent>? OnGamepadInputHandled;

    /// <summary>
    /// Raises the <see cref="OnGamepadInputHandled"/> event.
    /// </summary>
    public static void RaiseGamepadInputHandled(GamepadInputEvent e)
    {
        OnGamepadInputHandled?.Invoke(e);
    }

    #endregion

    #region Focus Events

    /// <summary>
    /// Raised when UI focus changes between elements or regions.
    /// </summary>
    public static event Action<FocusChangedEvent>? OnFocusChanged;

    /// <summary>
    /// Raises the <see cref="OnFocusChanged"/> event.
    /// </summary>
    public static void RaiseFocusChanged(FocusChangedEvent e)
    {
        OnFocusChanged?.Invoke(e);
    }

    #endregion

    #region Menu Events

    /// <summary>
    /// Raised when the game transitions between menu modes.
    /// </summary>
    public static event Action<MenuModeChangedEvent>? OnMenuModeChanged;

    /// <summary>
    /// Raises the <see cref="OnMenuModeChanged"/> event.
    /// </summary>
    public static void RaiseMenuModeChanged(MenuModeChangedEvent e)
    {
        OnMenuModeChanged?.Invoke(e);
    }

    /// <summary>
    /// Raised when entering or leaving the game menu (main menu vs in-game).
    /// </summary>
    public static event Action<GameMenuStateChangedEvent>? OnGameMenuStateChanged;

    /// <summary>
    /// Raises the <see cref="OnGameMenuStateChanged"/> event.
    /// </summary>
    public static void RaiseGameMenuStateChanged(GameMenuStateChangedEvent e)
    {
        OnGameMenuStateChanged?.Invoke(e);
    }

    #endregion

    #region Inventory Events

    /// <summary>
    /// Raised when the player's inventory opens or closes.
    /// </summary>
    public static event Action<InventoryStateChangedEvent>? OnInventoryStateChanged;

    /// <summary>
    /// Raises the <see cref="OnInventoryStateChanged"/> event.
    /// </summary>
    public static void RaiseInventoryStateChanged(InventoryStateChangedEvent e)
    {
        OnInventoryStateChanged?.Invoke(e);
    }

    /// <summary>
    /// Raised when a chest or storage container is opened or closed.
    /// </summary>
    public static event Action<ChestStateChangedEvent>? OnChestStateChanged;

    /// <summary>
    /// Raises the <see cref="OnChestStateChanged"/> event.
    /// </summary>
    public static void RaiseChestStateChanged(ChestStateChangedEvent e)
    {
        OnChestStateChanged?.Invoke(e);
    }

    #endregion

    #region Input Mode Events

    /// <summary>
    /// Raised when the input mode changes (keyboard, gamepad, etc.).
    /// </summary>
    public static event Action<InputModeChangedEvent>? OnInputModeChanged;

    /// <summary>
    /// Raises the <see cref="OnInputModeChanged"/> event.
    /// </summary>
    public static void RaiseInputModeChanged(InputModeChangedEvent e)
    {
        OnInputModeChanged?.Invoke(e);
    }

    /// <summary>
    /// Raised when text input focus begins or ends (chat, signs, etc.).
    /// </summary>
    public static event Action<TextInputStateChangedEvent>? OnTextInputStateChanged;

    /// <summary>
    /// Raises the <see cref="OnTextInputStateChanged"/> event.
    /// </summary>
    public static void RaiseTextInputStateChanged(TextInputStateChangedEvent e)
    {
        OnTextInputStateChanged?.Invoke(e);
    }

    #endregion

    #region Speech Events

    /// <summary>
    /// Raised when a speech announcement is made.
    /// Useful for logging, debugging, or coordinating announcements.
    /// </summary>
    public static event Action<SpeechAnnouncedEvent>? OnSpeechAnnounced;

    /// <summary>
    /// Raises the <see cref="OnSpeechAnnounced"/> event.
    /// </summary>
    public static void RaiseSpeechAnnounced(SpeechAnnouncedEvent e)
    {
        OnSpeechAnnounced?.Invoke(e);
    }

    /// <summary>
    /// Raised when speech is interrupted.
    /// </summary>
    public static event Action<SpeechInterruptedEvent>? OnSpeechInterrupted;

    /// <summary>
    /// Raises the <see cref="OnSpeechInterrupted"/> event.
    /// </summary>
    public static void RaiseSpeechInterrupted(SpeechInterruptedEvent e)
    {
        OnSpeechInterrupted?.Invoke(e);
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Clears all event subscriptions.
    /// Should be called during mod unload to prevent memory leaks.
    /// </summary>
    public static void ClearAll()
    {
        OnGamepadInputHandled = null;
        OnFocusChanged = null;
        OnMenuModeChanged = null;
        OnGameMenuStateChanged = null;
        OnInventoryStateChanged = null;
        OnChestStateChanged = null;
        OnInputModeChanged = null;
        OnTextInputStateChanged = null;
        OnSpeechAnnounced = null;
        OnSpeechInterrupted = null;
    }

    #endregion
}

#region Event Types

/// <summary>
/// Event data for gamepad input handling.
/// </summary>
/// <param name="SourceSystem">The name of the system that handled the input.</param>
/// <param name="Timestamp">When the input was handled.</param>
public readonly record struct GamepadInputEvent(
    string SourceSystem,
    DateTime Timestamp)
{
    /// <summary>
    /// Creates a new event with the current timestamp.
    /// </summary>
    public static GamepadInputEvent Now(string sourceSystem) =>
        new(sourceSystem, DateTime.UtcNow);
}

/// <summary>
/// Event data for UI focus changes.
/// </summary>
/// <param name="PreviousFocus">Description of the previously focused element, if any.</param>
/// <param name="NewFocus">Description of the newly focused element, if any.</param>
/// <param name="Region">The UI region where the focus change occurred.</param>
public readonly record struct FocusChangedEvent(
    string? PreviousFocus,
    string? NewFocus,
    FocusRegion Region);

/// <summary>
/// Identifies the UI region where focus resides.
/// </summary>
public enum FocusRegion
{
    /// <summary>No specific region or unknown.</summary>
    None,
    /// <summary>Main menu navigation.</summary>
    MainMenu,
    /// <summary>Player inventory grid.</summary>
    Inventory,
    /// <summary>Hotbar slots.</summary>
    Hotbar,
    /// <summary>Crafting recipe list.</summary>
    Crafting,
    /// <summary>Chest or storage container.</summary>
    Chest,
    /// <summary>NPC dialogue or shop.</summary>
    NpcInteraction,
    /// <summary>Settings or options menu.</summary>
    Settings,
    /// <summary>Mod configuration menu.</summary>
    ModConfig,
    /// <summary>World cursor position.</summary>
    WorldCursor,
}

/// <summary>
/// Event data for menu mode changes.
/// </summary>
/// <param name="PreviousMode">The previous menu mode value.</param>
/// <param name="NewMode">The new menu mode value.</param>
public readonly record struct MenuModeChangedEvent(
    int PreviousMode,
    int NewMode);

/// <summary>
/// Event data for game menu state changes.
/// </summary>
/// <param name="IsInGameMenu">True if now in the main menu; false if in-game.</param>
public readonly record struct GameMenuStateChangedEvent(
    bool IsInGameMenu);

/// <summary>
/// Event data for inventory state changes.
/// </summary>
/// <param name="IsOpen">True if the inventory is now open; false if closed.</param>
public readonly record struct InventoryStateChangedEvent(
    bool IsOpen);

/// <summary>
/// Event data for chest/storage state changes.
/// </summary>
/// <param name="IsOpen">True if a chest is now open; false if closed.</param>
/// <param name="ChestIndex">The index of the opened chest, or -1 if closed.</param>
/// <param name="ChestType">A description of the chest type (e.g., "Chest", "Piggy Bank").</param>
public readonly record struct ChestStateChangedEvent(
    bool IsOpen,
    int ChestIndex,
    string? ChestType);

/// <summary>
/// Event data for input mode changes.
/// </summary>
/// <param name="PreviousMode">The previous input mode.</param>
/// <param name="NewMode">The new input mode.</param>
public readonly record struct InputModeChangedEvent(
    InputModeType PreviousMode,
    InputModeType NewMode);

/// <summary>
/// Simplified input mode classification.
/// </summary>
public enum InputModeType
{
    /// <summary>Standard keyboard and mouse input.</summary>
    Keyboard,
    /// <summary>Keyboard with UI navigation mode active.</summary>
    KeyboardUI,
    /// <summary>Physical gamepad input.</summary>
    Gamepad,
    /// <summary>Gamepad with UI navigation mode active.</summary>
    GamepadUI,
}

/// <summary>
/// Event data for text input state changes.
/// </summary>
/// <param name="IsActive">True if text input is now active; false if ended.</param>
/// <param name="Context">Description of the text input context (e.g., "Chat", "Sign", "ChestName").</param>
public readonly record struct TextInputStateChangedEvent(
    bool IsActive,
    string Context);

/// <summary>
/// Event data for speech announcements.
/// </summary>
/// <param name="Text">The text that was announced.</param>
/// <param name="Category">The announcement category.</param>
/// <param name="WasInterrupting">Whether the announcement interrupted previous speech.</param>
public readonly record struct SpeechAnnouncedEvent(
    string Text,
    string Category,
    bool WasInterrupting);

/// <summary>
/// Event data for speech interruption.
/// </summary>
/// <param name="Source">The source that triggered the interruption.</param>
public readonly record struct SpeechInterruptedEvent(
    string Source);

#endregion
