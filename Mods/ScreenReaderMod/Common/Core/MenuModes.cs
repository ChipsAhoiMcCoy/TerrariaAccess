#nullable enable

namespace ScreenReaderMod.Common.Core;

/// <summary>
/// Terraria and tModLoader menu mode identifiers.
/// These correspond to Main.menuMode values.
/// Use these constants instead of magic numbers throughout the codebase.
/// </summary>
/// <remarks>
/// Terraria uses MenuID from Terraria.ID for some modes, but many are undocumented.
/// This class provides a centralized reference with descriptive names.
/// </remarks>
public static class MenuModes
{
    #region Main Menus (Vanilla Terraria)

    /// <summary>
    /// Title screen / main menu (menuMode = 0).
    /// Same as MenuID.Title.
    /// </summary>
    public const int Title = 0;

    /// <summary>
    /// Single player character selection (menuMode = 1).
    /// </summary>
    public const int PlayerSelect = 1;

    /// <summary>
    /// Multiplayer menu / character selection (menuMode = 2).
    /// </summary>
    public const int Multiplayer = 2;

    /// <summary>
    /// World deletion confirmation (menuMode = 9).
    /// Same as MenuID.WorldDeletionConfirmation.
    /// </summary>
    public const int WorldDeletionConfirmation = 9;

    /// <summary>
    /// World loading / connection status screen (menuMode = 10).
    /// </summary>
    public const int WorldLoading = 10;

    /// <summary>
    /// Settings menu root (menuMode = 11).
    /// </summary>
    public const int Settings = 11;

    /// <summary>
    /// Multiplayer join options (menuMode = 12).
    /// </summary>
    public const int MultiplayerJoin = 12;

    /// <summary>
    /// Server IP address entry (menuMode = 13).
    /// Same as MenuID.ServerIP.
    /// </summary>
    public const int ServerIp = 13;

    /// <summary>
    /// Server status / connection status (menuMode = 14).
    /// </summary>
    public const int ServerStatus = 14;

    /// <summary>
    /// Controls menu (menuMode = 17).
    /// </summary>
    public const int Controls = 17;

    /// <summary>
    /// Credits screen (menuMode = 18).
    /// </summary>
    public const int Credits = 18;

    /// <summary>
    /// Server password entry (menuMode = 31).
    /// Same as MenuID.ServerPasswordRequested.
    /// </summary>
    public const int ServerPassword = 31;

    /// <summary>
    /// Character deletion confirmation (menuMode = 28).
    /// Same as MenuID.CharacterDeletion.
    /// </summary>
    public const int CharacterDeletion = 28;

    /// <summary>
    /// Character deletion second confirmation (menuMode = 29).
    /// Same as MenuID.CharacterDeletionConfirmation.
    /// </summary>
    public const int CharacterDeletionConfirmation = 29;

    /// <summary>
    /// Server port entry (menuMode = 131).
    /// Same as MenuID.ServerPort.
    /// </summary>
    public const int ServerPort = 131;

    #endregion

    #region Settings Submenus (Vanilla Terraria)

    /// <summary>
    /// Audio settings menu (menuMode = 26).
    /// Contains volume sliders and audio options.
    /// </summary>
    public const int AudioSettings = 26;

    /// <summary>
    /// Resolution selection menu (menuMode = 111).
    /// </summary>
    public const int Resolution = 111;

    /// <summary>
    /// General settings menu (menuMode = 112).
    /// Contains autopause, autosave, and other general options.
    /// </summary>
    public const int GeneralSettings = 112;

    /// <summary>
    /// Video settings menu (menuMode = 1111).
    /// Contains fullscreen, frameskip, lighting, and quality settings.
    /// </summary>
    public const int VideoSettings = 1111;

    /// <summary>
    /// Interface/UI settings menu (menuMode = 1112).
    /// Contains HUD and interface options.
    /// </summary>
    public const int InterfaceSettings = 1112;

    /// <summary>
    /// Cursor settings menu (menuMode = 1125).
    /// Contains cursor color and smart cursor options.
    /// </summary>
    public const int CursorSettings = 1125;

    /// <summary>
    /// Gameplay settings menu (menuMode = 1127).
    /// Contains placement preview and other gameplay options.
    /// </summary>
    public const int GameplaySettings = 1127;

    /// <summary>
    /// Language selection menu (menuMode = 1212).
    /// First language selection screen (no back option).
    /// </summary>
    public const int LanguageSelect = 1212;

    /// <summary>
    /// Language selection menu with back option (menuMode = 1213).
    /// </summary>
    public const int LanguageSelectWithBack = 1213;

    /// <summary>
    /// Effects/particles quality menu (menuMode = 2008).
    /// Contains heat distortion, storm effects, etc.
    /// </summary>
    public const int EffectsSettings = 2008;

    #endregion

    #region tModLoader Menus

    /// <summary>
    /// tModLoader FancyUI container mode (menuMode = 888).
    /// Used when tModLoader UI states are active (mods, mod browser, etc.).
    /// Same as MenuID.FancyUI.
    /// </summary>
    public const int FancyUI = 888;

    /// <summary>
    /// Host and Play server settings (menuMode = 889).
    /// Server configuration before starting multiplayer host.
    /// </summary>
    public const int HostAndPlay = 889;

    /// <summary>
    /// tModLoader settings menu (menuMode = 10017).
    /// Contains mod-specific settings like auto-reload, zoom, etc.
    /// </summary>
    public const int TModLoaderSettings = 10017;

    /// <summary>
    /// Mod configuration editor (menuMode = 10024).
    /// Active when editing a specific mod's config.
    /// </summary>
    public const int ModConfigEdit = 10024;

    /// <summary>
    /// Mod configuration list (menuMode = 10027).
    /// Shows list of available mod configurations.
    /// </summary>
    public const int ModConfigList = 10027;

    #endregion

    #region Helper Methods

    /// <summary>
    /// Checks if the given menu mode is a settings submenu.
    /// </summary>
    public static bool IsSettingsMenu(int menuMode)
    {
        return menuMode is AudioSettings
            or GeneralSettings
            or VideoSettings
            or InterfaceSettings
            or CursorSettings
            or GameplaySettings
            or EffectsSettings
            or Resolution
            or TModLoaderSettings;
    }

    /// <summary>
    /// Checks if the given menu mode is a deletion confirmation screen.
    /// </summary>
    public static bool IsDeletionMenu(int menuMode)
    {
        return menuMode is CharacterDeletion
            or CharacterDeletionConfirmation
            or WorldDeletionConfirmation;
    }

    /// <summary>
    /// Checks if the given menu mode is a mod configuration screen.
    /// </summary>
    public static bool IsModConfigMenu(int menuMode)
    {
        return menuMode is ModConfigEdit
            or ModConfigList
            or FancyUI; // FancyUI hosts mod config states
    }

    /// <summary>
    /// Checks if the given menu mode is a server connection menu.
    /// </summary>
    public static bool IsServerConnectionMenu(int menuMode)
    {
        return menuMode is ServerIp
            or ServerPort
            or ServerPassword
            or ServerStatus
            or WorldLoading;
    }

    /// <summary>
    /// Checks if the given menu mode should defer Lang.menu fallback.
    /// Some modes intentionally expose no menuItems array.
    /// </summary>
    public static bool ShouldDeferLangMenuFallback(int menuMode)
    {
        return menuMode is PlayerSelect
            or Multiplayer
            or WorldLoading
            or ServerStatus
            or FancyUI
            or ServerIp
            or ServerPort
            or ServerPassword;
    }

    #endregion
}
