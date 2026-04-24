#nullable enable
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.Journey;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.GameContent.Creative;

namespace TerrariaAccess.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    private sealed class JourneyResearchNarrator
    {
        private int _lastItemId = int.MinValue;
        private int _lastHave;
        private int _lastNeeded;
        private bool _lastFullyResearched;
        private bool _lastCanResearch;

        public void Update(Player player)
        {
            if (player is null || !Main.CreativeMenu.Enabled)
            {
                Reset();
                return;
            }

            Item? sacrifice = JourneyReflection.TryGetSacrificeItem();
            if (sacrifice is null || sacrifice.IsAir)
            {
                if (_lastItemId != 0)
                {
                    _lastItemId = 0;
                    _lastHave = 0;
                    _lastNeeded = 0;
                    _lastFullyResearched = false;
                    _lastCanResearch = false;
                }
                return;
            }

            int itemId = sacrifice.type;
            var tracker = player.creativeTracker?.ItemSacrifices;
            if (tracker is null)
            {
                return;
            }

            bool canResearch = CreativeItemSacrificesCatalog.Instance
                .TryGetSacrificeCountCapToUnlockInfiniteItems(itemId, out _);
            bool hasNumbers = tracker.TryGetSacrificeNumbers(itemId, out int have, out int needed);
            bool fully = canResearch && hasNumbers && needed > 0 && have >= needed;

            if (itemId == _lastItemId &&
                fully == _lastFullyResearched &&
                canResearch == _lastCanResearch &&
                have == _lastHave &&
                needed == _lastNeeded)
            {
                return;
            }

            string itemName = sacrifice.AffixName();
            string message;
            if (fully)
            {
                message = string.Format(
                    LocalizationHelper.GetTextOrFallback(
                        "Mods.TerrariaAccess.JourneyMode.Sacrifice.FullyResearched",
                        "Sacrifice slot: {0}, fully researched"),
                    itemName);
            }
            else if (!canResearch)
            {
                message = string.Format(
                    LocalizationHelper.GetTextOrFallback(
                        "Mods.TerrariaAccess.JourneyMode.Sacrifice.CannotResearch",
                        "Sacrifice slot: {0}, cannot be researched"),
                    itemName);
            }
            else if (hasNumbers)
            {
                message = string.Format(
                    LocalizationHelper.GetTextOrFallback(
                        "Mods.TerrariaAccess.JourneyMode.Sacrifice.ProgressFormat",
                        "Sacrifice slot: {0}, {1} of {2} to research"),
                    itemName,
                    have,
                    needed);
            }
            else
            {
                message = string.Format(
                    LocalizationHelper.GetTextOrFallback(
                        "Mods.TerrariaAccess.JourneyMode.Sacrifice.CannotResearch",
                        "Sacrifice slot: {0}, cannot be researched"),
                    itemName);
            }

            bool justCompleted = fully && !_lastFullyResearched && _lastItemId == itemId && _lastItemId > 0;

            _lastItemId = itemId;
            _lastHave = have;
            _lastNeeded = needed;
            _lastFullyResearched = fully;
            _lastCanResearch = canResearch;

            ScreenReaderService.Announce(message, force: justCompleted);

            if (justCompleted)
            {
                ScreenReaderService.Announce(
                    LocalizationHelper.GetTextOrFallback(
                        "Mods.TerrariaAccess.JourneyMode.Sacrifice.Completed",
                        "Fully researched"),
                    force: true);
            }
        }

        public void AnnounceCurrent(Player player)
        {
            Reset();
            Update(player);
        }

        private void Reset()
        {
            _lastItemId = int.MinValue;
            _lastHave = 0;
            _lastNeeded = 0;
            _lastFullyResearched = false;
            _lastCanResearch = false;
        }
    }
}
