#nullable enable
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.ModBrowser;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Systems.Journey;

public sealed class JourneyInputSystem : ModSystem
{
    private string? _lastInfiniteItemsSearchText;

    public override void PostUpdateInput()
    {
        if (Main.dedServ || Main.gameMenu)
        {
            return;
        }

        if (Main.LocalPlayer is not { active: true })
        {
            return;
        }

        bool wasSearchActive = SearchModeManager.IsSearchModeActive;
        SearchModeManager.Update(enqueueSearchPrefix: false);
        bool isSearchActive = SearchModeManager.IsSearchModeActive;
        HandleInfiniteItemsSearchSync(wasSearchActive, isSearchActive);
    }

    private void HandleInfiniteItemsSearchSync(bool wasSearchActive, bool isSearchActive)
    {
        UISearchBar? searchBar = JourneyReflection.TryGetInfiniteItemsSearchBar();
        if (searchBar is null)
        {
            _lastInfiniteItemsSearchText = null;
            return;
        }

        if (isSearchActive && !wasSearchActive)
        {
            if (searchBar.IsWritingText)
            {
                searchBar.ToggleTakingText();
                Mod.Logger.Info("[JourneySearch] Took over orphaned infinite-items search text input");
            }

            _lastInfiniteItemsSearchText = null;
            ScreenReaderService.ClearAllPrefixes();
            ScreenReaderService.Announce(
                LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.SearchMode.SearchEnabled",
                    "Search mode. Type to filter. Press Tab to return to navigation"),
                force: true);
        }
        else if (!isSearchActive && wasSearchActive)
        {
            if (searchBar.IsWritingText)
            {
                searchBar.ToggleTakingText();
                Mod.Logger.Info("[JourneySearch] Deactivated infinite-items search text input");
            }

            _lastInfiniteItemsSearchText = null;
            EndOwnedTextInput(searchBar);
        }
        else if (!isSearchActive && searchBar.IsWritingText)
        {
            searchBar.ToggleTakingText();
            _lastInfiniteItemsSearchText = null;
            Mod.Logger.Info("[JourneySearch] Deactivated orphaned infinite-items search input");
        }

        if (SearchModeManager.IsSearchModeActive)
        {
            ProcessOwnedInfiniteItemsSearchInput(searchBar);
        }
        else
        {
            _lastInfiniteItemsSearchText = null;
        }
    }

    private void ProcessOwnedInfiniteItemsSearchInput(UISearchBar searchBar)
    {
        // UISearchBar.ToggleTakingText opens Terraria's gamepad virtual keyboard whenever
        // gamepad UI hints are visible. Journey mode treats that as a fancy UI transition
        // and closes the creative menu, so we feed the search contents directly instead.
        PlayerInput.CurrentInputMode = Terraria.GameInput.InputMode.Keyboard;
        PlayerInput.WritingText = true;
        Main.CurrentInputTextTakerOverride = searchBar;

        string currentText = JourneyReflection.TryGetInfiniteItemsSearchString() ?? string.Empty;
        string updatedText = Main.GetInputText(currentText);

        if (Main.inputTextEscape)
        {
            Main.inputTextEscape = false;
            SearchModeManager.ExitSearchMode();
            EndOwnedTextInput(searchBar);
            return;
        }

        if (!string.Equals(updatedText, currentText, System.StringComparison.Ordinal))
        {
            searchBar.SetContents(updatedText);
        }

        string? currentSearchText = JourneyReflection.TryGetInfiniteItemsSearchString();
        if (_lastInfiniteItemsSearchText is not null &&
            !string.Equals(currentSearchText, _lastInfiniteItemsSearchText, System.StringComparison.Ordinal))
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
        }

        _lastInfiniteItemsSearchText = currentSearchText;
    }

    private static void EndOwnedTextInput(UISearchBar searchBar)
    {
        if (ReferenceEquals(Main.CurrentInputTextTakerOverride, searchBar))
        {
            Main.CurrentInputTextTakerOverride = null;
        }

        PlayerInput.WritingText = false;
    }
}
