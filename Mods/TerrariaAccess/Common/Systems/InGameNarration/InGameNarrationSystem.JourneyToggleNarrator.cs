#nullable enable
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;
using Terraria;

namespace TerrariaAccess.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class JourneyToggleNarrator
    {
        private bool _wasEnabled;
        private bool _wasShowingResearch;

        public void Update()
        {
            bool isEnabled = Main.CreativeMenu.Enabled;

            if (isEnabled && !_wasEnabled)
            {
                ScreenReaderService.Announce(
                    LocalizationHelper.GetTextOrFallback(
                        "Mods.TerrariaAccess.JourneyMode.Toggle.Opened",
                        "Journey menu opened"),
                    force: true);
                _wasShowingResearch = SafeIsShowingResearchMenu();
                _wasEnabled = true;
                return;
            }

            if (!isEnabled && _wasEnabled)
            {
                ScreenReaderService.Announce(
                    LocalizationHelper.GetTextOrFallback(
                        "Mods.TerrariaAccess.JourneyMode.Toggle.Closed",
                        "Journey menu closed"),
                    force: true);
                _wasEnabled = false;
                _wasShowingResearch = false;
                return;
            }

            if (!isEnabled)
            {
                return;
            }

            bool showingResearch = SafeIsShowingResearchMenu();
            if (showingResearch != _wasShowingResearch)
            {
                string key = showingResearch
                    ? "Mods.TerrariaAccess.JourneyMode.Panel.SwitchedToResearch"
                    : "Mods.TerrariaAccess.JourneyMode.Panel.SwitchedToDuplication";
                string fallback = showingResearch ? "Switched to Research panel" : "Switched to Duplication panel";
                ScreenReaderService.Announce(
                    LocalizationHelper.GetTextOrFallback(key, fallback),
                    force: true);
                _wasShowingResearch = showingResearch;
            }
        }

        private static bool SafeIsShowingResearchMenu()
        {
            try
            {
                return Main.CreativeMenu.IsShowingResearchMenu();
            }
            catch
            {
                return false;
            }
        }
    }
}
