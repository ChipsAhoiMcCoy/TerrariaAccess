#nullable enable
using System.Text;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.GameContent.Creative;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Systems.Journey;

public sealed class JourneyInputSystem : ModSystem
{
    public override void PostUpdateInput()
    {
        if (Main.dedServ || Main.gameMenu)
        {
            return;
        }

        if (Main.LocalPlayer is not Player player || !player.active)
        {
            return;
        }

        if (JourneyModeKeybinds.ToggleMenu?.JustPressed == true)
        {
            HandleToggleMenu(player);
        }

        if (!Main.CreativeMenu.Enabled)
        {
            return;
        }

        if (JourneyModeKeybinds.ReadSacrificeProgress?.JustPressed == true)
        {
            AnnounceSacrificeProgress(player);
        }

        if (JourneyModeKeybinds.PanelStateCheck?.JustPressed == true)
        {
            AnnouncePanelStateSummary(player);
        }

        if (JourneyModeKeybinds.CyclePowerCategory?.JustPressed == true)
        {
            CyclePowerCategory();
        }
    }

    private static void HandleToggleMenu(Player player)
    {
        try
        {
            if (player.difficulty != 3)
            {
                ScreenReaderService.Announce(
                    LocalizationHelper.GetTextOrFallback(
                        "Mods.TerrariaAccess.JourneyMode.Toggle.NotJourneyMode",
                        "Journey menu is only available for Journey mode characters"),
                    force: true);
                return;
            }

            if (player.dead)
            {
                return;
            }

            if (player.chest != -1)
            {
                ScreenReaderService.Announce(
                    LocalizationHelper.GetTextOrFallback(
                        "Mods.TerrariaAccess.JourneyMode.Toggle.BlockedByChest",
                        "Close the chest before opening the Journey menu"),
                    force: true);
                return;
            }

            // Route through Player.ToggleCreativeMenu so the inventory is opened first;
            // otherwise DrawInterface_27_Inventory closes the menu the next frame.
            player.ToggleCreativeMenu();
        }
        catch
        {
        }
    }

    private static void AnnounceSacrificeProgress(Player player)
    {
        Item? sacrifice = JourneyReflection.TryGetSacrificeItem();
        if (sacrifice is null || sacrifice.IsAir)
        {
            ScreenReaderService.Announce(
                LocalizationHelper.GetTextOrFallback(
                    "Mods.TerrariaAccess.JourneyMode.Sacrifice.Empty",
                    "Sacrifice slot empty, place an item to see research progress"),
                force: true);
            return;
        }

        int itemId = sacrifice.type;
        var tracker = player.creativeTracker?.ItemSacrifices;
        if (tracker is null)
        {
            return;
        }

        string itemName = sacrifice.AffixName();
        string message;

        bool canResearch = CreativeItemSacrificesCatalog.Instance
            .TryGetSacrificeCountCapToUnlockInfiniteItems(itemId, out _);
        bool hasNumbers = tracker.TryGetSacrificeNumbers(itemId, out int have, out int needed);
        bool fully = canResearch && hasNumbers && needed > 0 && have >= needed;

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

        ScreenReaderService.Announce(message, force: true);
    }

    private static void AnnouncePanelStateSummary(Player player)
    {
        StringBuilder sb = new();

        bool showingResearch = false;
        try { showingResearch = Main.CreativeMenu.IsShowingResearchMenu(); } catch { }
        string panelKey = showingResearch
            ? "Mods.TerrariaAccess.JourneyMode.Panel.Research"
            : "Mods.TerrariaAccess.JourneyMode.Panel.Duplication";
        string panelFallback = showingResearch ? "Research" : "Duplication";
        sb.Append(LocalizationHelper.GetTextOrFallback(panelKey, panelFallback));

        foreach (JourneyPowerEntry entry in JourneyPowerRegistry.All)
        {
            object? power = JourneyPowersReflection.TryGetPower(entry.Key);
            if (power is null) continue;

            string label = LocalizationHelper.GetTextOrFallback(
                $"Mods.TerrariaAccess.JourneyMode.Power.{entry.LocSuffix}",
                entry.FallbackLabel);

            switch (entry.Kind)
            {
                case JourneyPowerKind.Toggle:
                case JourneyPowerKind.Shared:
                {
                    bool? state = JourneyPowersReflection.TryGetTogglePerPlayerState(power, player.whoAmI);
                    if (!state.HasValue) continue;
                    if (!state.Value) continue;
                    string onWord = LocalizationHelper.GetTextOrFallback(
                        "Mods.TerrariaAccess.JourneyMode.State.On", "on");
                    sb.Append(", ").Append(label).Append(' ').Append(onWord);
                    break;
                }
                case JourneyPowerKind.Slider:
                {
                    float? value = JourneyPowersReflection.TryGetSliderValue(power, player.whoAmI);
                    if (!value.HasValue) continue;
                    string formatted = JourneySliderValueFormatter.Format(entry.Key, value.Value);
                    sb.Append(", ").Append(label).Append(' ').Append(formatted);
                    break;
                }
            }
        }

        ScreenReaderService.Announce(sb.ToString(), force: true);
    }

    private static void CyclePowerCategory()
    {
        int option = JourneyReflection.TryGetCurrentPowersCategoryOption() ?? 0;
        string tabLocKey = option switch
        {
            1 => "Mods.TerrariaAccess.JourneyMode.Powers.Tab.InfiniteItems",
            2 => "Mods.TerrariaAccess.JourneyMode.Powers.Tab.Research",
            3 => "Mods.TerrariaAccess.JourneyMode.Powers.Tab.Time",
            4 => "Mods.TerrariaAccess.JourneyMode.Powers.Tab.Weather",
            5 => "Mods.TerrariaAccess.JourneyMode.Powers.Tab.EnemyDifficulty",
            6 => "Mods.TerrariaAccess.JourneyMode.Powers.Tab.PersonalPowers",
            _ => "Mods.TerrariaAccess.JourneyMode.Panel.Duplication",
        };
        string tabFallback = option switch
        {
            1 => "Infinite Items",
            2 => "Research",
            3 => "Time",
            4 => "Weather",
            5 => "Enemy Difficulty",
            6 => "Personal Powers",
            _ => "Duplication",
        };

        ScreenReaderService.Announce(
            LocalizationHelper.GetTextOrFallback(tabLocKey, tabFallback),
            force: true);
    }
}
