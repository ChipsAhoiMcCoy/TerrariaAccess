#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.UI.ModBrowser;
using Terraria.Social.Steam;
using Terraria.UI;
using ScreenReaderMod.Common.Utilities;

namespace ScreenReaderMod.Common.Systems.MenuNarration;

internal sealed partial class MenuUiSelectionTracker
{
    private static string DescribeModDownloadItem(UIElement element)
    {
        if (!TryGetData(element, out ModDownloadItem? modData) || modData is null)
        {
            return string.Empty;
        }

        string name = !string.IsNullOrWhiteSpace(modData.DisplayNameClean)
            ? modData.DisplayNameClean
            : modData.DisplayName ?? modData.ModName ?? "Mod";

        string author = TextSanitizer.Clean(modData.Author);
        string side = DescribeModSide(modData.ModSide);
        string status = DescribeInstallStatus(modData);

        var parts = new List<string>(6)
        {
            TextSanitizer.Clean(name),
        };

        if (!string.IsNullOrWhiteSpace(author))
        {
            parts.Add($"by {author}");
        }

        if (!string.IsNullOrWhiteSpace(side))
        {
            parts.Add(side);
        }

        if (modData.Banned)
        {
            parts.Add("Banned on Workshop");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            parts.Add(status);
        }
        else if (modData.Version is not null)
        {
            parts.Add($"Version {modData.Version}");
        }

        return TextSanitizer.JoinWithComma(parts.ToArray());
    }

    private static string DescribeInstallStatus(ModDownloadItem modData)
    {
        if (modData.AppNeedRestartToReinstall)
        {
            return "Restart required to reinstall";
        }

        if (modData.NeedUpdate)
        {
            string to = modData.Version?.ToString() ?? string.Empty;
            return string.IsNullOrWhiteSpace(to) ? "Update available" : $"Update available to {to}";
        }

        if (modData.IsInstalled)
        {
            return string.IsNullOrWhiteSpace(modData.Version?.ToString()) ? "Installed" : $"Installed, available {modData.Version}";
        }

        return string.IsNullOrWhiteSpace(modData.Version?.ToString()) ? "Not installed" : $"Not installed, version {modData.Version}";
    }

    private static string DescribeModSide(ModSide side)
    {
        return side switch
        {
            ModSide.Both => "Client and server",
            ModSide.Client => "Client only",
            ModSide.Server => "Server only",
            ModSide.NoSync => "No sync",
            _ => string.Empty,
        };
    }

    private static string DescribeBrowserFilterToggle(UIElement element)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        PropertyInfo? stateProperty = element.GetType().GetProperty("State", flags);
        object? stateValue = stateProperty?.GetValue(element);
        Type? stateType = stateValue?.GetType();
        if (stateType is null || !stateType.IsEnum)
        {
            return string.Empty;
        }

        string? label = stateValue switch
        {
            SearchFilter search => DescribeSearchFilter(search),
            ModBrowserSortMode sort => DescribeSortMode(sort),
            ModBrowserTimePeriod period => DescribeTimePeriod(period),
            UpdateFilter update => DescribeUpdateFilter(update),
            ModSideFilter side => DescribeModSideFilter(side),
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        return TextSanitizer.Clean(label);
    }

    private static string DescribeModBrowserButton(UIElement element)
    {
        UIElement? modItem = FindAncestor(element, static type => type.FullName == "Terraria.ModLoader.UI.ModBrowser.UIModDownloadItem");
        if (modItem is null)
        {
            return string.Empty;
        }

        Type modItemType = modItem.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        bool Matches(string fieldName)
        {
            try
            {
                FieldInfo? field = modItemType.GetField(fieldName, flags);
                return field?.GetValue(modItem) is UIElement target && ReferenceEquals(target, element);
            }
            catch
            {
                return false;
            }
        }

        if (Matches("_moreInfoButton"))
        {
            return DescribeModInfo(modItemType, modItem);
        }

        if (Matches("_updateWithDepsButton"))
        {
            return DescribeUpdateWithDependencies(modItemType, modItem);
        }

        if (Matches("_updateButton"))
        {
            return "Restart required to reinstall";
        }

        if (Matches("tMLUpdateRequired"))
        {
            return "Requires tModLoader update";
        }

        string? typeName = element.GetType().FullName;
        if (typeName is not null && typeName.Contains("UIHoverImage", StringComparison.OrdinalIgnoreCase))
        {
            return "View dependencies";
        }

        return string.Empty;
    }

    private static string DescribeModInfo(Type modItemType, UIElement modItem)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        try
        {
            PropertyInfo? property = modItemType.GetProperty("ViewModInfoText", flags);
            if (property?.GetValue(modItem) is string value && !string.IsNullOrWhiteSpace(value))
            {
                return TextSanitizer.Clean(value);
            }
        }
        catch
        {
            // ignore
        }

        return "More info";
    }

    private static string DescribeUpdateWithDependencies(Type modItemType, UIElement modItem)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        try
        {
            PropertyInfo? property = modItemType.GetProperty("UpdateWithDepsText", flags);
            if (property?.GetValue(modItem) is string value && !string.IsNullOrWhiteSpace(value))
            {
                return TextSanitizer.Clean(value);
            }
        }
        catch
        {
            // ignore
        }

        return "Download with dependencies";
    }

    private static string DescribeSearchFilter(SearchFilter state)
    {
        return state switch
        {
            SearchFilter.Name => "Search by name",
            SearchFilter.Author => "Search by author",
            _ => $"Search filter {state}",
        };
    }

    private static string DescribeSortMode(ModBrowserSortMode state)
    {
        return state switch
        {
            ModBrowserSortMode.DownloadsDescending => "Sort by downloads",
            ModBrowserSortMode.RecentlyPublished => "Sort by recently published",
            ModBrowserSortMode.RecentlyUpdated => "Sort by recently updated",
            ModBrowserSortMode.Hot => "Sort by hot mods",
            _ => $"Sort mode {state}",
        };
    }

    private static string DescribeTimePeriod(ModBrowserTimePeriod state)
    {
        return state switch
        {
            ModBrowserTimePeriod.Today => "Time period: today",
            ModBrowserTimePeriod.OneWeek => "Time period: past week",
            ModBrowserTimePeriod.ThreeMonths => "Time period: past three months",
            ModBrowserTimePeriod.SixMonths => "Time period: past six months",
            ModBrowserTimePeriod.OneYear => "Time period: past year",
            ModBrowserTimePeriod.AllTime => "Time period: all time",
            _ => $"Time period {state}",
        };
    }

    private static string DescribeUpdateFilter(UpdateFilter state)
    {
        return state switch
        {
            UpdateFilter.All => "Updates: all mods",
            UpdateFilter.Available => "Updates: available",
            UpdateFilter.UpdateOnly => "Updates: update only",
            UpdateFilter.InstalledOnly => "Updates: installed only",
            _ => $"Updates filter {state}",
        };
    }

    private static string DescribeModSideFilter(ModSideFilter state)
    {
        return state switch
        {
            ModSideFilter.All => "Side filter: all",
            ModSideFilter.Both => "Side filter: client and server",
            ModSideFilter.Client => "Side filter: client only",
            ModSideFilter.Server => "Side filter: server only",
            ModSideFilter.NoSync => "Side filter: no sync",
            _ => $"Side filter {state}",
        };
    }

    private static string DescribeTagFilterToggle(UIElement element)
    {
        UIElement? browser = FindAncestor(element, static type => type.FullName == "Terraria.ModLoader.UI.ModBrowser.UIModBrowser");
        if (browser is null)
        {
            return string.Empty;
        }

        HashSet<int>? categories = null;
        int languageTag = -1;

        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo? categoryField = browser.GetType().GetField("CategoryTagsFilter", flags);
            if (categoryField?.GetValue(browser) is HashSet<int> set)
            {
                categories = set;
            }

            FieldInfo? languageField = browser.GetType().GetField("LanguageTagFilter", flags);
            if (languageField?.GetValue(browser) is int lang)
            {
                languageTag = lang;
            }
        }
        catch
        {
            // ignore lookup issues
        }

        string selection = DescribeTagSelection(categories, languageTag);
        if (string.IsNullOrWhiteSpace(selection))
        {
            selection = "none selected";
        }

        return TextSanitizer.Clean($"Tag filters: {selection}");
    }

    private static string DescribeTagSelection(HashSet<int>? categories, int languageTag)
    {
        var parts = new List<string>(2);
        if (categories is not null && categories.Count > 0)
        {
            string categoryNames = ResolveTagNames(categories);
            parts.Add(string.IsNullOrWhiteSpace(categoryNames) ? $"{categories.Count} categories" : $"Categories {categoryNames}");
        }

        if (languageTag >= 0)
        {
            string languageName = ResolveTagName(languageTag);
            parts.Add(string.IsNullOrWhiteSpace(languageName) ? "Language selected" : $"Language {languageName}");
        }

        return TextSanitizer.JoinWithComma(parts.ToArray());
    }

    private static string ResolveTagNames(IEnumerable<int> indices)
    {
        var names = new List<string>();
        int count = 0;
        foreach (int index in indices)
        {
            count++;
            string name = ResolveTagName(index);
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }

            if (names.Count >= 3)
            {
                break;
            }
        }

        if (names.Count == 0)
        {
            return string.Empty;
        }

        int remaining = Math.Max(0, count - names.Count);
        string label = string.Join(", ", names);
        if (remaining > 0)
        {
            label += $" (+{remaining} more)";
        }

        return label;
    }

    private static string ResolveTagName(int index)
    {
        try
        {
            if (index >= 0 && index < SteamedWraps.ModTags.Count)
            {
                string name = Language.GetTextValue(SteamedWraps.ModTags[index].NameKey);
                return TextSanitizer.Clean(name);
            }
        }
        catch
        {
            // ignore lookup failures
        }

        return string.Empty;
    }
}
