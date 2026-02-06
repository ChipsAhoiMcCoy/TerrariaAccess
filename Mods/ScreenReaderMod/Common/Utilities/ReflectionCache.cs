#nullable enable
using System;
using System.Reflection;
using Terraria.Localization;

namespace ScreenReaderMod.Common.Utilities;

/// <summary>
/// Centralized cache for reflection handles to tModLoader and Terraria internal types.
/// Uses lazy initialization to defer reflection until first access.
/// Organizes reflection handles by their source type for maintainability.
/// </summary>
internal static class ReflectionCache
{
    private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags InstancePublic = BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags StaticPublic = BindingFlags.Static | BindingFlags.Public;

    #region UIMods (Manage Mods Menu)

    /// <summary>
    /// Reflection handles for Terraria.ModLoader.UI.UIMods (Manage Mods screen).
    /// </summary>
    internal static class UIMods
    {
        private static readonly Lazy<Type?> _type = new(() =>
            Type.GetType("Terraria.ModLoader.UI.UIMods, tModLoader"));

        private static readonly Lazy<FieldInfo?> _modList = new(() =>
            Type?.GetField("modList", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _items = new(() =>
            Type?.GetField("items", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _categoryButtons = new(() =>
            Type?.GetField("_categoryButtons", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _buttonEA = new(() =>
            Type?.GetField("buttonEA", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _buttonDA = new(() =>
            Type?.GetField("buttonDA", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _buttonRM = new(() =>
            Type?.GetField("buttonRM", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _buttonB = new(() =>
            Type?.GetField("buttonB", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _buttonOMF = new(() =>
            Type?.GetField("buttonOMF", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _buttonCL = new(() =>
            Type?.GetField("buttonCL", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _uiScrollbar = new(() =>
            Type?.GetField("uIScrollbar", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _sortMode = new(() =>
            Type?.GetField("sortMode", InstancePublic));

        private static readonly Lazy<FieldInfo?> _enabledFilterMode = new(() =>
            Type?.GetField("enabledFilterMode", InstancePublic));

        private static readonly Lazy<FieldInfo?> _modSideFilterMode = new(() =>
            Type?.GetField("modSideFilterMode", InstancePublic));

        private static readonly Lazy<FieldInfo?> _searchFilterMode = new(() =>
            Type?.GetField("searchFilterMode", InstancePublic));

        private static readonly Lazy<FieldInfo?> _blockInput = new(() =>
            Type?.GetField("_blockInput", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _confirmDialogYesButton = new(() =>
            Type?.GetField("_confirmDialogYesButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _confirmDialogNoButton = new(() =>
            Type?.GetField("_confirmDialogNoButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _confirmDialogText = new(() =>
            Type?.GetField("_confirmDialogText", InstanceNonPublic));

        private static readonly Lazy<MethodInfo?> _closeConfirmDialog = new(() =>
            Type?.GetMethod("CloseConfirmDialog", InstanceNonPublic));

        internal static Type? Type => _type.Value;
        internal static FieldInfo? ModList => _modList.Value;
        internal static FieldInfo? Items => _items.Value;
        internal static FieldInfo? CategoryButtons => _categoryButtons.Value;
        internal static FieldInfo? ButtonEA => _buttonEA.Value;
        internal static FieldInfo? ButtonDA => _buttonDA.Value;
        internal static FieldInfo? ButtonRM => _buttonRM.Value;
        internal static FieldInfo? ButtonB => _buttonB.Value;
        internal static FieldInfo? ButtonOMF => _buttonOMF.Value;
        internal static FieldInfo? ButtonCL => _buttonCL.Value;
        internal static FieldInfo? UiScrollbar => _uiScrollbar.Value;
        internal static FieldInfo? SortMode => _sortMode.Value;
        internal static FieldInfo? EnabledFilterMode => _enabledFilterMode.Value;
        internal static FieldInfo? ModSideFilterMode => _modSideFilterMode.Value;
        internal static FieldInfo? SearchFilterMode => _searchFilterMode.Value;
        internal static FieldInfo? BlockInput => _blockInput.Value;
        internal static FieldInfo? ConfirmDialogYesButton => _confirmDialogYesButton.Value;
        internal static FieldInfo? ConfirmDialogNoButton => _confirmDialogNoButton.Value;
        internal static FieldInfo? ConfirmDialogText => _confirmDialogText.Value;
        internal static MethodInfo? CloseConfirmDialog => _closeConfirmDialog.Value;
    }

    #endregion

    #region UIModItem (Manage Mods - Individual Mod Entry)

    /// <summary>
    /// Reflection handles for Terraria.ModLoader.UI.UIModItem (individual mod in Manage Mods list).
    /// </summary>
    internal static class UIModItem
    {
        private static readonly Lazy<Type?> _type = new(() =>
            Type.GetType("Terraria.ModLoader.UI.UIModItem, tModLoader"));

        private static readonly Lazy<FieldInfo?> _mod = new(() =>
            Type?.GetField("_mod", InstanceNonPublic));

        private static readonly Lazy<PropertyInfo?> _displayNameClean = new(() =>
            Type?.GetProperty("DisplayNameClean", InstancePublic));

        private static readonly Lazy<PropertyInfo?> _modName = new(() =>
            Type?.GetProperty("ModName", InstancePublic));

        private static readonly Lazy<FieldInfo?> _uiModStateText = new(() =>
            Type?.GetField("_uiModStateText", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _moreInfoButton = new(() =>
            Type?.GetField("_moreInfoButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _deleteModButton = new(() =>
            Type?.GetField("_deleteModButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _configButton = new(() =>
            Type?.GetField("_configButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _dialogYesButton = new(() =>
            Type?.GetField("_dialogYesButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _dialogNoButton = new(() =>
            Type?.GetField("_dialogNoButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _dialogText = new(() =>
            Type?.GetField("_dialogText", InstanceNonPublic));

        internal static Type? Type => _type.Value;
        internal static FieldInfo? Mod => _mod.Value;
        internal static PropertyInfo? DisplayNameClean => _displayNameClean.Value;
        internal static PropertyInfo? ModName => _modName.Value;
        internal static FieldInfo? UiModStateText => _uiModStateText.Value;
        internal static FieldInfo? MoreInfoButton => _moreInfoButton.Value;
        internal static FieldInfo? DeleteModButton => _deleteModButton.Value;
        internal static FieldInfo? ConfigButton => _configButton.Value;
        internal static FieldInfo? DialogYesButton => _dialogYesButton.Value;
        internal static FieldInfo? DialogNoButton => _dialogNoButton.Value;
        internal static FieldInfo? DialogText => _dialogText.Value;
    }

    #endregion

    #region LocalMod (Mod Metadata)

    /// <summary>
    /// Reflection handles for Terraria.ModLoader.Core.LocalMod (mod metadata).
    /// </summary>
    internal static class LocalMod
    {
        private static readonly Lazy<Type?> _type = new(() =>
            Type.GetType("Terraria.ModLoader.Core.LocalMod, tModLoader"));

        private static readonly Lazy<PropertyInfo?> _enabled = new(() =>
            Type?.GetProperty("Enabled", InstancePublic));

        internal static Type? Type => _type.Value;
        internal static PropertyInfo? Enabled => _enabled.Value;
    }

    #endregion

    #region UIModBrowser (Download Mods Menu)

    /// <summary>
    /// Reflection handles for Terraria.ModLoader.UI.ModBrowser.UIModBrowser (Download Mods screen).
    /// </summary>
    internal static class UIModBrowser
    {
        private static readonly Lazy<Type?> _type = new(() =>
            Type.GetType("Terraria.ModLoader.UI.ModBrowser.UIModBrowser, tModLoader"));

        private static readonly Lazy<FieldInfo?> _modList = new(() =>
            Type?.GetField("ModList", InstancePublic));

        private static readonly Lazy<FieldInfo?> _receivedItems = new(() =>
            Type?.GetField("_receivedItems", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _categoryButtons = new(() =>
            Type?.GetField("CategoryButtons", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _reloadButton = new(() =>
            Type?.GetField("_reloadButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _backButton = new(() =>
            Type?.GetField("_backButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _clearButton = new(() =>
            Type?.GetField("_clearButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _downloadAllButton = new(() =>
            Type?.GetField("_downloadAllButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _updateAllButton = new(() =>
            Type?.GetField("_updateAllButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _tagFilterToggle = new(() =>
            Type?.GetField("TagFilterToggle", InstancePublic));

        internal static Type? Type => _type.Value;
        internal static FieldInfo? ModList => _modList.Value;
        internal static FieldInfo? ReceivedItems => _receivedItems.Value;
        internal static FieldInfo? CategoryButtons => _categoryButtons.Value;
        internal static FieldInfo? ReloadButton => _reloadButton.Value;
        internal static FieldInfo? BackButton => _backButton.Value;
        internal static FieldInfo? ClearButton => _clearButton.Value;
        internal static FieldInfo? DownloadAllButton => _downloadAllButton.Value;
        internal static FieldInfo? UpdateAllButton => _updateAllButton.Value;
        internal static FieldInfo? TagFilterToggle => _tagFilterToggle.Value;
    }

    #endregion

    #region UIModDownloadItem (Download Mods - Individual Mod Entry)

    /// <summary>
    /// Reflection handles for Terraria.ModLoader.UI.ModBrowser.UIModDownloadItem (individual mod in Download list).
    /// </summary>
    internal static class UIModDownloadItem
    {
        private static readonly Lazy<Type?> _type = new(() =>
            Type.GetType("Terraria.ModLoader.UI.ModBrowser.UIModDownloadItem, tModLoader"));

        private static readonly Lazy<FieldInfo?> _modDownload = new(() =>
            Type?.GetField("ModDownload", InstancePublic));

        private static readonly Lazy<FieldInfo?> _moreInfoButton = new(() =>
            Type?.GetField("_moreInfoButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _updateButton = new(() =>
            Type?.GetField("_updateButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _updateWithDepsButton = new(() =>
            Type?.GetField("_updateWithDepsButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _tMLUpdateRequired = new(() =>
            Type?.GetField("tMLUpdateRequired", InstanceNonPublic));

        internal static Type? Type => _type.Value;
        internal static FieldInfo? ModDownload => _modDownload.Value;
        internal static FieldInfo? MoreInfoButton => _moreInfoButton.Value;
        internal static FieldInfo? UpdateButton => _updateButton.Value;
        internal static FieldInfo? UpdateWithDepsButton => _updateWithDepsButton.Value;
        internal static FieldInfo? TMLUpdateRequired => _tMLUpdateRequired.Value;
    }

    #endregion

    #region UIModInfo (Mod Info Screen)

    /// <summary>
    /// Reflection handles for Terraria.ModLoader.UI.UIModInfo (Mod Info/Details screen).
    /// </summary>
    internal static class UIModInfo
    {
        private static readonly Lazy<Type?> _type = new(() =>
            Type.GetType("Terraria.ModLoader.UI.UIModInfo, tModLoader"));

        private static readonly Lazy<FieldInfo?> _modInfo = new(() =>
            Type?.GetField("_modInfo", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _uITextPanel = new(() =>
            Type?.GetField("_uITextPanel", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _modHomepageButton = new(() =>
            Type?.GetField("_modHomepageButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _modSteamButton = new(() =>
            Type?.GetField("_modSteamButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _extractLocalizationButton = new(() =>
            Type?.GetField("extractLocalizationButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _fakeExtractLocalizationButton = new(() =>
            Type?.GetField("fakeExtractLocalizationButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _extractButton = new(() =>
            Type?.GetField("_extractButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _deleteButton = new(() =>
            Type?.GetField("_deleteButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _fakeDeleteButton = new(() =>
            Type?.GetField("_fakeDeleteButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _uIElement = new(() =>
            Type?.GetField("_uIElement", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _modDisplayName = new(() =>
            Type?.GetField("_modDisplayName", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _info = new(() =>
            Type?.GetField("_info", InstanceNonPublic));

        internal static Type? Type => _type.Value;
        internal static FieldInfo? ModInfo => _modInfo.Value;
        internal static FieldInfo? UITextPanel => _uITextPanel.Value;
        internal static FieldInfo? ModHomepageButton => _modHomepageButton.Value;
        internal static FieldInfo? ModSteamButton => _modSteamButton.Value;
        internal static FieldInfo? ExtractLocalizationButton => _extractLocalizationButton.Value;
        internal static FieldInfo? FakeExtractLocalizationButton => _fakeExtractLocalizationButton.Value;
        internal static FieldInfo? ExtractButton => _extractButton.Value;
        internal static FieldInfo? DeleteButton => _deleteButton.Value;
        internal static FieldInfo? FakeDeleteButton => _fakeDeleteButton.Value;
        internal static FieldInfo? UIElement => _uIElement.Value;
        internal static FieldInfo? ModDisplayName => _modDisplayName.Value;
        internal static FieldInfo? Info => _info.Value;
    }

    #endregion

    #region UIModConfigList (Mod Config Selection Screen)

    /// <summary>
    /// Reflection handles for Terraria.ModLoader.Config.UI.UIModConfigList (config mod selection screen).
    /// </summary>
    internal static class UIModConfigList
    {
        private static readonly Lazy<Type?> _type = new(() =>
            Type.GetType("Terraria.ModLoader.Config.UI.UIModConfigList, tModLoader"));

        private static readonly Lazy<FieldInfo?> _selectedMod = new(() =>
            Type?.GetField("selectedMod", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _modList = new(() =>
            Type?.GetField("modList", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _configList = new(() =>
            Type?.GetField("configList", InstanceNonPublic));

        internal static Type? Type => _type.Value;
        internal static FieldInfo? SelectedMod => _selectedMod.Value;
        internal static FieldInfo? ModList => _modList.Value;
        internal static FieldInfo? ConfigList => _configList.Value;
    }

    #endregion

    #region UIModConfig (Mod Config Editing Screen)

    /// <summary>
    /// Reflection handles for Terraria.ModLoader.Config.UI.UIModConfig (config editing screen).
    /// </summary>
    internal static class UIModConfig
    {
        private static readonly Lazy<Type?> _type = new(() =>
            Type.GetType("Terraria.ModLoader.Config.UI.UIModConfig, tModLoader"));

        private static readonly Lazy<FieldInfo?> _mod = new(() =>
            Type?.GetField("mod", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _modConfig = new(() =>
            Type?.GetField("modConfig", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _mainConfigList = new(() =>
            Type?.GetField("mainConfigList", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _uIList = new(() =>
            Type?.GetField("uIList", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _saveConfigButton = new(() =>
            Type?.GetField("saveConfigButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _backButton = new(() =>
            Type?.GetField("backButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _revertConfigButton = new(() =>
            Type?.GetField("revertConfigButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _restoreDefaultsConfigButton = new(() =>
            Type?.GetField("restoreDefaultsConfigButton", InstanceNonPublic));

        internal static Type? Type => _type.Value;
        internal static FieldInfo? Mod => _mod.Value;
        internal static FieldInfo? ModConfig => _modConfig.Value;
        internal static FieldInfo? MainConfigList => _mainConfigList.Value;
        internal static FieldInfo? UIList => _uIList.Value;
        internal static FieldInfo? SaveConfigButton => _saveConfigButton.Value;
        internal static FieldInfo? BackButton => _backButton.Value;
        internal static FieldInfo? RevertConfigButton => _revertConfigButton.Value;
        internal static FieldInfo? RestoreDefaultsConfigButton => _restoreDefaultsConfigButton.Value;
    }

    #endregion

    #region Mod and ModConfig (tModLoader Core Types)

    /// <summary>
    /// Reflection handles for Terraria.ModLoader.Mod (mod instance).
    /// </summary>
    internal static class Mod
    {
        private static readonly Lazy<Type?> _type = new(() =>
            Type.GetType("Terraria.ModLoader.Mod, tModLoader"));

        private static readonly Lazy<PropertyInfo?> _displayName = new(() =>
            Type?.GetProperty("DisplayName", InstancePublic));

        private static readonly Lazy<PropertyInfo?> _name = new(() =>
            Type?.GetProperty("Name", InstancePublic));

        internal static Type? Type => _type.Value;
        internal static PropertyInfo? DisplayName => _displayName.Value;
        internal static PropertyInfo? Name => _name.Value;
    }

    /// <summary>
    /// Reflection handles for Terraria.ModLoader.Config.ModConfig (config definition).
    /// </summary>
    internal static class ModConfigDef
    {
        private static readonly Lazy<Type?> _type = new(() =>
            Type.GetType("Terraria.ModLoader.Config.ModConfig, tModLoader"));

        private static readonly Lazy<PropertyInfo?> _displayName = new(() =>
            Type?.GetProperty("DisplayName", InstancePublic));

        private static readonly Lazy<PropertyInfo?> _fullName = new(() =>
            Type?.GetProperty("FullName", InstancePublic));

        internal static Type? Type => _type.Value;
        internal static PropertyInfo? DisplayName => _displayName.Value;
        internal static PropertyInfo? FullName => _fullName.Value;
    }

    /// <summary>
    /// Reflection handles for Terraria.ModLoader.Config.UI.ConfigElement (config UI element base).
    /// </summary>
    internal static class ConfigElement
    {
        private static readonly Lazy<Type?> _type = new(() =>
            Type.GetType("Terraria.ModLoader.Config.UI.ConfigElement, tModLoader"));

        internal static Type? Type => _type.Value;
    }

    /// <summary>
    /// Reflection handles for Terraria.ModLoader.UI.UIModConfigItem (mod config item).
    /// </summary>
    internal static class UIModConfigItem
    {
        private static readonly Lazy<Type?> _type = new(() =>
            Type.GetType("Terraria.ModLoader.UI.UIModConfigItem, tModLoader"));

        internal static Type? Type => _type.Value;
    }

    /// <summary>
    /// Reflection handles for Terraria.ModLoader.UI.UIButton (generic button type).
    /// </summary>
    internal static class UIButton
    {
        private static readonly Lazy<Type?> _type = new(() =>
            Type.GetType("Terraria.ModLoader.UI.UIButton`1, tModLoader"));

        internal static Type? Type => _type.Value;
    }

    #endregion

    #region UIModPacks (Mod Packs Screen)

    /// <summary>
    /// Reflection handles for Terraria.ModLoader.UI.UIModPacks (Mod Packs collection screen).
    /// </summary>
    internal static class UIModPacks
    {
        private static readonly Lazy<Type?> _type = new(() =>
            Type.GetType("Terraria.ModLoader.UI.UIModPacks, tModLoader"));

        private static readonly Lazy<FieldInfo?> _modPacks = new(() =>
            Type?.GetField("_modPacks", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _scrollPanel = new(() =>
            Type?.GetField("_scrollPanel", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _uiLoader = new(() =>
            Type?.GetField("_uiLoader", InstanceNonPublic));

        internal static Type? Type => _type.Value;
        internal static FieldInfo? ModPacks => _modPacks.Value;
        internal static FieldInfo? ScrollPanel => _scrollPanel.Value;
        internal static FieldInfo? UiLoader => _uiLoader.Value;
    }

    #endregion

    #region UIModPackItem (Mod Pack Item in List)

    /// <summary>
    /// Reflection handles for Terraria.ModLoader.UI.UIModPackItem (individual mod pack in list).
    /// </summary>
    internal static class UIModPackItem
    {
        private static readonly Lazy<Type?> _type = new(() =>
            Type.GetType("Terraria.ModLoader.UI.UIModPackItem, tModLoader"));

        private static readonly Lazy<FieldInfo?> _filename = new(() =>
            Type?.GetField("_filename", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _mods = new(() =>
            Type?.GetField("_mods", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _numMods = new(() =>
            Type?.GetField("_numMods", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _numModsEnabled = new(() =>
            Type?.GetField("_numModsEnabled", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _numModsDisabled = new(() =>
            Type?.GetField("_numModsDisabled", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _missing = new(() =>
            Type?.GetField("_missing", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _legacy = new(() =>
            Type?.GetField("_legacy", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _enableListButton = new(() =>
            Type?.GetField("_enableListButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _enableListOnlyButton = new(() =>
            Type?.GetField("_enableListOnlyButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _viewInModBrowserButton = new(() =>
            Type?.GetField("_viewInModBrowserButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _updateListWithEnabledButton = new(() =>
            Type?.GetField("_updateListWithEnabledButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _deleteButton = new(() =>
            Type?.GetField("_deleteButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _fakeDeleteButton = new(() =>
            Type?.GetField("_fakeDeleteButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _importFromPackLocalButton = new(() =>
            Type?.GetField("_importFromPackLocalButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _removePackLocalButton = new(() =>
            Type?.GetField("_removePackLocalButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _exportPackInstanceButton = new(() =>
            Type?.GetField("_exportPackInstanceButton", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _removePackInstanceButton = new(() =>
            Type?.GetField("_removePackInstanceButton", InstanceNonPublic));

        internal static Type? Type => _type.Value;
        internal static FieldInfo? Filename => _filename.Value;
        internal static FieldInfo? Mods => _mods.Value;
        internal static FieldInfo? NumMods => _numMods.Value;
        internal static FieldInfo? NumModsEnabled => _numModsEnabled.Value;
        internal static FieldInfo? NumModsDisabled => _numModsDisabled.Value;
        internal static FieldInfo? Missing => _missing.Value;
        internal static FieldInfo? Legacy => _legacy.Value;
        internal static FieldInfo? EnableListButton => _enableListButton.Value;
        internal static FieldInfo? EnableListOnlyButton => _enableListOnlyButton.Value;
        internal static FieldInfo? ViewInModBrowserButton => _viewInModBrowserButton.Value;
        internal static FieldInfo? UpdateListWithEnabledButton => _updateListWithEnabledButton.Value;
        internal static FieldInfo? DeleteButton => _deleteButton.Value;
        internal static FieldInfo? FakeDeleteButton => _fakeDeleteButton.Value;
        internal static FieldInfo? ImportFromPackLocalButton => _importFromPackLocalButton.Value;
        internal static FieldInfo? RemovePackLocalButton => _removePackLocalButton.Value;
        internal static FieldInfo? ExportPackInstanceButton => _exportPackInstanceButton.Value;
        internal static FieldInfo? RemovePackInstanceButton => _removePackInstanceButton.Value;
    }

    #endregion

    #region UIModSources (Mod Sources Screen)

    /// <summary>
    /// Reflection handles for Terraria.ModLoader.UI.UIModSources (Mod development sources screen).
    /// </summary>
    internal static class UIModSources
    {
        private static readonly Lazy<Type?> _type = new(() =>
            Type.GetType("Terraria.ModLoader.UI.UIModSources, tModLoader"));

        private static readonly Lazy<FieldInfo?> _modList = new(() =>
            Type?.GetField("_modList", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _items = new(() =>
            Type?.GetField("_items", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _uIPanel = new(() =>
            Type?.GetField("_uIPanel", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _uIElement = new(() =>
            Type?.GetField("_uIElement", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _filterTextBox = new(() =>
            Type?.GetField("filterTextBox", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _links = new(() =>
            Type?.GetField("_links", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _uiLoader = new(() =>
            Type?.GetField("_uiLoader", InstanceNonPublic));

        internal static Type? Type => _type.Value;
        internal static FieldInfo? ModList => _modList.Value;
        internal static FieldInfo? Items => _items.Value;
        internal static FieldInfo? UIPanel => _uIPanel.Value;
        internal static FieldInfo? UIElement => _uIElement.Value;
        internal static FieldInfo? FilterTextBox => _filterTextBox.Value;
        internal static FieldInfo? Links => _links.Value;
        internal static FieldInfo? UiLoader => _uiLoader.Value;
    }

    #endregion

    #region UIModSourceItem (Mod Source Item in List)

    /// <summary>
    /// Reflection handles for Terraria.ModLoader.UI.UIModSourceItem (individual mod source in list).
    /// </summary>
    internal static class UIModSourceItem
    {
        private static readonly Lazy<Type?> _type = new(() =>
            Type.GetType("Terraria.ModLoader.UI.UIModSourceItem, tModLoader"));

        private static readonly Lazy<FieldInfo?> _mod = new(() =>
            Type?.GetField("_mod", InstanceNonPublic));

        private static readonly Lazy<FieldInfo?> _modName = new(() =>
            Type?.GetField("modName", InstancePublic));

        private static readonly Lazy<FieldInfo?> _builtMod = new(() =>
            Type?.GetField("_builtMod", InstanceNonPublic));

        internal static Type? Type => _type.Value;
        internal static FieldInfo? Mod => _mod.Value;
        internal static FieldInfo? ModName => _modName.Value;
        internal static FieldInfo? BuiltMod => _builtMod.Value;
    }

    #endregion

    #region LocalizedText (Terraria Localization)

    /// <summary>
    /// Reflection handles for Terraria.Localization.LocalizedText.
    /// </summary>
    internal static class LocalizedTextRef
    {
        private static readonly Lazy<PropertyInfo?> _value = new(() =>
            typeof(LocalizedText).GetProperty("Value", InstancePublic));

        internal static PropertyInfo? Value => _value.Value;
    }

    #endregion

    #region Validation

    /// <summary>
    /// Logs warnings for any missing reflection handles that are required for core functionality.
    /// Call this during initialization to detect compatibility issues early.
    /// </summary>
    internal static void LogMissingHandles(Action<string> logger)
    {
        if (UIMods.Type is null)
        {
            logger("[ReflectionCache] UIMods type not found - Manage Mods accessibility will be limited");
        }

        if (UIModBrowser.Type is null)
        {
            logger("[ReflectionCache] UIModBrowser type not found - Download Mods accessibility will be limited");
        }

        if (UIModInfo.Type is null)
        {
            logger("[ReflectionCache] UIModInfo type not found - Mod Info accessibility will be limited");
        }

        if (UIModConfigList.Type is null)
        {
            logger("[ReflectionCache] UIModConfigList type not found - Mod Config accessibility will be limited");
        }

        if (UIModPacks.Type is null)
        {
            logger("[ReflectionCache] UIModPacks type not found - Mod Packs accessibility will be limited");
        }

        if (UIModSources.Type is null)
        {
            logger("[ReflectionCache] UIModSources type not found - Mod Sources accessibility will be limited");
        }
    }

    #endregion
}
