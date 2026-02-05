#nullable enable
using System;
using Terraria;
using Terraria.GameInput;

namespace ScreenReaderMod.Common.Core;

/// <summary>
/// Unified input state tracking that consolidates scattered input checks.
/// Provides a single source of truth for input-related state queries.
/// </summary>
/// <remarks>
/// This class centralizes input state that was previously scattered across
/// multiple systems (GamepadEmulation, MenuNarration, etc.), making it easier
/// to reason about input handling and reducing duplicate code.
///
/// Usage:
/// <code>
/// // Check if text input is active before processing navigation
/// if (InputState.IsTextInputActive)
///     return;
///
/// // Get a complete snapshot for complex decisions
/// var state = InputState.Current;
/// if (state.UsingGamepadUI &amp;&amp; !state.InMenu)
/// {
///     // Handle in-game gamepad navigation
/// }
/// </code>
///
/// Thread safety: All properties access game state and should only be
/// called from the main game thread.
/// </remarks>
public static class InputState
{
    #region Text Input State

    /// <summary>
    /// Returns true if text input is currently active (chat, sign editing, chest naming, etc.).
    /// When true, navigation input should typically be suppressed to allow typing.
    /// </summary>
    public static bool IsTextInputActive
    {
        get
        {
            // Chat input active
            if (Main.drawingPlayerChat)
            {
                return true;
            }

            // Sign editing
            if (Main.editSign)
            {
                return true;
            }

            // Chest renaming
            if (Main.editChest)
            {
                return true;
            }

            // Any other text input override (mod UIs, etc.)
            if (Main.CurrentInputTextTakerOverride is not null)
            {
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Gets a description of the current text input context, or null if not active.
    /// </summary>
    public static string? TextInputContext
    {
        get
        {
            if (Main.drawingPlayerChat)
            {
                return "Chat";
            }

            if (Main.editSign)
            {
                return "Sign";
            }

            if (Main.editChest)
            {
                return "ChestName";
            }

            if (Main.CurrentInputTextTakerOverride is not null)
            {
                return "TextInput";
            }

            return null;
        }
    }

    #endregion

    #region Input Mode State

    /// <summary>
    /// Gets the current Terraria input mode.
    /// </summary>
    public static InputMode CurrentInputMode => PlayerInput.CurrentInputMode;

    /// <summary>
    /// Returns true if using keyboard or keyboard UI input mode.
    /// </summary>
    public static bool IsKeyboardMode
    {
        get
        {
            InputMode mode = PlayerInput.CurrentInputMode;
            return mode == InputMode.Keyboard || mode == InputMode.KeyboardUI;
        }
    }

    /// <summary>
    /// Returns true if using gamepad or gamepad UI input mode.
    /// </summary>
    public static bool IsGamepadMode
    {
        get
        {
            InputMode mode = PlayerInput.CurrentInputMode;
            return mode == InputMode.XBoxGamepad || mode == InputMode.XBoxGamepadUI;
        }
    }

    /// <summary>
    /// Returns true if using any UI navigation mode (keyboard UI or gamepad UI).
    /// </summary>
    public static bool IsUiNavigationMode
    {
        get
        {
            InputMode mode = PlayerInput.CurrentInputMode;
            return mode == InputMode.KeyboardUI || mode == InputMode.XBoxGamepadUI;
        }
    }

    /// <summary>
    /// Returns true if Terraria's gamepad UI mode is active.
    /// This enables D-pad/stick navigation in menus.
    /// </summary>
    public static bool UsingGamepadUI => PlayerInput.UsingGamepadUI;

    /// <summary>
    /// Converts the current Terraria input mode to our simplified type.
    /// </summary>
    public static InputModeType CurrentModeType
    {
        get
        {
            return PlayerInput.CurrentInputMode switch
            {
                InputMode.Keyboard => InputModeType.Keyboard,
                InputMode.KeyboardUI => InputModeType.KeyboardUI,
                InputMode.XBoxGamepad => InputModeType.Gamepad,
                InputMode.XBoxGamepadUI => InputModeType.GamepadUI,
                _ => InputModeType.Keyboard,
            };
        }
    }

    #endregion

    #region Game State

    /// <summary>
    /// Returns true if the player is in the main menu.
    /// </summary>
    public static bool InGameMenu => Main.gameMenu;

    /// <summary>
    /// Returns true if the player's inventory is open.
    /// </summary>
    public static bool InventoryOpen => Main.playerInventory;

    /// <summary>
    /// Returns true if the game is paused (autopause with inventory open, or in settings).
    /// </summary>
    public static bool IsPaused => Main.gamePaused;

    /// <summary>
    /// Returns true if in-game options window is open.
    /// </summary>
    public static bool InGameOptionsOpen => Main.ingameOptionsWindow;

    /// <summary>
    /// Returns true if a chest or storage container is currently open.
    /// </summary>
    public static bool ChestOpen
    {
        get
        {
            Player? player = LocalPlayer;
            return player is not null && player.chest != -1;
        }
    }

    /// <summary>
    /// Gets the index of the currently open chest, or -1 if none.
    /// </summary>
    public static int OpenChestIndex
    {
        get
        {
            Player? player = LocalPlayer;
            return player?.chest ?? -1;
        }
    }

    /// <summary>
    /// Returns true if the player is talking to an NPC.
    /// </summary>
    public static bool TalkingToNpc
    {
        get
        {
            Player? player = LocalPlayer;
            return player is not null && player.talkNPC != -1;
        }
    }

    /// <summary>
    /// Returns true if an NPC shop is open.
    /// </summary>
    public static bool ShopOpen => Main.npcShop != 0;

    /// <summary>
    /// Returns true if the guide's crafting menu is open.
    /// </summary>
    public static bool GuideCraftMenuOpen => Main.InGuideCraftMenu;

    /// <summary>
    /// Returns true if the Goblin Tinkerer's reforge menu is open.
    /// </summary>
    public static bool ReforgeMenuOpen => Main.InReforgeMenu;

    /// <summary>
    /// Returns true if any "fancy" UI state is visible (MenuUI or InGameUI).
    /// </summary>
    public static bool FancyUiActive
    {
        get
        {
            if (Main.MenuUI?.IsVisible ?? false)
            {
                return true;
            }

            return Main.InGameUI?.IsVisible ?? false;
        }
    }

    #endregion

    #region Player State

    /// <summary>
    /// Gets the local player, or null if not available.
    /// </summary>
    public static Player? LocalPlayer
    {
        get
        {
            if (Main.myPlayer < 0 || Main.myPlayer >= Main.maxPlayers)
            {
                return null;
            }

            Player player = Main.player[Main.myPlayer];
            return player?.active == true ? player : null;
        }
    }

    /// <summary>
    /// Returns true if there is an active local player.
    /// </summary>
    public static bool HasActivePlayer => LocalPlayer is not null;

    #endregion

    #region Snapshot

    /// <summary>
    /// Gets a snapshot of the current input state.
    /// Useful when you need to check multiple state values consistently.
    /// </summary>
    public static InputStateSnapshot Current => new(
        IsTextInputActive: IsTextInputActive,
        TextInputContext: TextInputContext,
        InputMode: CurrentModeType,
        UsingGamepadUI: UsingGamepadUI,
        InMenu: InGameMenu,
        InventoryOpen: InventoryOpen,
        IsPaused: IsPaused,
        ChestOpen: ChestOpen,
        TalkingToNpc: TalkingToNpc,
        ShopOpen: ShopOpen,
        FancyUiActive: FancyUiActive);

    #endregion

    #region UI Context Helpers

    /// <summary>
    /// Returns true if any UI overlay is open that would require navigation handling.
    /// Combines checks for inventory, settings, fancy UI, NPC interaction, etc.
    /// </summary>
    public static bool AnyUiOpen
    {
        get
        {
            if (InventoryOpen || InGameOptionsOpen || FancyUiActive)
            {
                return true;
            }

            Player? player = LocalPlayer;
            if (player is null)
            {
                return false;
            }

            return player.talkNPC != -1 ||
                   player.sign != -1 ||
                   player.chest != -1 ||
                   Main.npcShop != 0 ||
                   Main.InGuideCraftMenu ||
                   Main.InReforgeMenu ||
                   Main.CreativeMenu.Enabled ||
                   Main.hairWindow ||
                   Main.clothesWindow ||
                   player.tileEntityAnchor.InUse;
        }
    }

    /// <summary>
    /// Returns true if the game state suggests gamepad UI navigation should be enabled.
    /// </summary>
    public static bool ShouldEnableGamepadUiNavigation
    {
        get
        {
            // Always enable in menu
            if (InGameMenu)
            {
                return true;
            }

            // Enable when any UI is open
            return AnyUiOpen;
        }
    }

    #endregion
}

/// <summary>
/// An immutable snapshot of input state at a point in time.
/// </summary>
/// <param name="IsTextInputActive">Whether text input was active.</param>
/// <param name="TextInputContext">The text input context, if active.</param>
/// <param name="InputMode">The current input mode type.</param>
/// <param name="UsingGamepadUI">Whether gamepad UI mode was active.</param>
/// <param name="InMenu">Whether in the main menu.</param>
/// <param name="InventoryOpen">Whether the inventory was open.</param>
/// <param name="IsPaused">Whether the game was paused.</param>
/// <param name="ChestOpen">Whether a chest was open.</param>
/// <param name="TalkingToNpc">Whether talking to an NPC.</param>
/// <param name="ShopOpen">Whether an NPC shop was open.</param>
/// <param name="FancyUiActive">Whether a fancy UI was active.</param>
public readonly record struct InputStateSnapshot(
    bool IsTextInputActive,
    string? TextInputContext,
    InputModeType InputMode,
    bool UsingGamepadUI,
    bool InMenu,
    bool InventoryOpen,
    bool IsPaused,
    bool ChestOpen,
    bool TalkingToNpc,
    bool ShopOpen,
    bool FancyUiActive)
{
    /// <summary>
    /// Returns true if using keyboard or keyboard UI mode.
    /// </summary>
    public bool IsKeyboardMode =>
        InputMode == InputModeType.Keyboard || InputMode == InputModeType.KeyboardUI;

    /// <summary>
    /// Returns true if using gamepad or gamepad UI mode.
    /// </summary>
    public bool IsGamepadMode =>
        InputMode == InputModeType.Gamepad || InputMode == InputModeType.GamepadUI;

    /// <summary>
    /// Returns true if any UI overlay is open.
    /// </summary>
    public bool AnyUiOpen =>
        InventoryOpen || ChestOpen || TalkingToNpc || ShopOpen || FancyUiActive;
}
