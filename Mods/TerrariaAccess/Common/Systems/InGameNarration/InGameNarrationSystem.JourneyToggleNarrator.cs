#nullable enable
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.Journey;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.UI.Gamepad;

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
                AnnouncePanelStateChange(_lastCategoryOption, categoryOption, ShouldSuppressPanelChangeForSliderActivation(categoryOption));
                _lastCategoryOption = categoryOption;
                return;
            }
        }

        private static void AnnouncePanelStateChange(int previousOption, int currentOption, bool suppressOpenedAnnouncement)
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

            if (suppressOpenedAnnouncement)
            {
                return;
            }

            ScreenReaderService.Announce(
                LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.JourneyMode.Panel.Opened",
                    "Panel opened"),
                force: true);
        }

        private static bool IsPanelOption(int option) => option is >= 1 and <= 6;

        private static bool ShouldSuppressPanelChangeForSliderActivation(int currentOption)
        {
            if (currentOption == 5)
            {
                return true;
            }

            int point = UILinkPointNavigator.CurrentPoint;
            return currentOption switch
            {
                3 => point is 10012 or 10013,
                4 => point is 10007 or 10009 or 10011,
                5 => point is 10006 or 10007,
                6 => point is 10009 or 10010,
                _ => false,
            };
        }

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
