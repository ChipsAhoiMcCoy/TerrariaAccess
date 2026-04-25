#nullable enable
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.Journey;
using TerrariaAccess.Common.Utilities;
using Terraria;

namespace TerrariaAccess.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class JourneyToggleNarrator
    {
        private bool _wasEnabled;
        private int _lastCategoryOption = -1;

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
                _lastCategoryOption = SafeGetCurrentCategoryOption();
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
                _lastCategoryOption = -1;
                return;
            }

            if (!isEnabled)
            {
                return;
            }

            int categoryOption = SafeGetCurrentCategoryOption();
            if (categoryOption != _lastCategoryOption)
            {
                AnnouncePanelStateChange(_lastCategoryOption, categoryOption);
                _lastCategoryOption = categoryOption;
                return;
            }
        }

        private static void AnnouncePanelStateChange(int previousOption, int currentOption)
        {
            bool wasPanelOpen = IsPanelOption(previousOption);
            bool isPanelOpen = IsPanelOption(currentOption);

            if (!isPanelOpen)
            {
                if (wasPanelOpen)
                {
                    ScreenReaderService.Announce(
                        LocalizationHelper.GetTextOrFallback(
                            "Mods.TerrariaAccess.JourneyMode.Panel.Closed",
                            "Panel closed"),
                        force: true);
                }
                return;
            }

            ScreenReaderService.Announce(
                LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.JourneyMode.Panel.Opened",
                    "Panel opened"),
                force: true);
        }

        private static bool IsPanelOption(int option) => option is >= 1 and <= 6;

        private static int SafeGetCurrentCategoryOption()
        {
            try
            {
                return JourneyReflection.TryGetCurrentPowersCategoryOption() ?? 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
