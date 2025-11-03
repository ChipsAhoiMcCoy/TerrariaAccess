using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

var baseDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string[] candidateDirectories =
{
    Path.Combine(baseDir, "tModLoader_dist"),
    Path.Combine(baseDir, "tModLoader"),
    Path.Combine(baseDir, "..", "tModLoader_dist"),
    Path.Combine(baseDir, "..", "tModLoader"),
    Path.Combine(AppContext.BaseDirectory, "tModLoader_dist"),
    Path.Combine(AppContext.BaseDirectory, "tModLoader"),
    @"C:\Program Files (x86)\Steam\steamapps\common\tModLoader",
};

string? tmlPath = candidateDirectories
    .Select(dir => Path.Combine(dir, "tModLoader.dll"))
    .FirstOrDefault(File.Exists);

if (tmlPath is null)
{
    Console.Error.WriteLine("Unable to locate tModLoader.dll. Place a copy under tModLoader_dist or install tModLoader via Steam.");
    return;
}

Console.WriteLine($"Loading {tmlPath}");

var assemblyDirectory = Path.GetDirectoryName(tmlPath)!;
var assemblyLookup = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

void AddAssembliesFrom(string directory, SearchOption searchOption)
{
    if (!Directory.Exists(directory))
    {
        return;
    }

    foreach (string file in Directory.GetFiles(directory, "*.dll", searchOption))
    {
        string fileName = Path.GetFileName(file);
        assemblyLookup.TryAdd(fileName, file);
    }
}

AddAssembliesFrom(assemblyDirectory, SearchOption.TopDirectoryOnly);
AddAssembliesFrom(Path.Combine(assemblyDirectory, "Libraries"), SearchOption.AllDirectories);
AddAssembliesFrom(Path.Combine(assemblyDirectory, "dotnet", "shared"), SearchOption.AllDirectories);

var loadContext = new AssemblyLoadContext("tml", isCollectible: false);
loadContext.Resolving += (_, assemblyName) =>
{
    string candidateFile = assemblyName.Name + ".dll";
    if (assemblyLookup.TryGetValue(candidateFile, out string? path) && File.Exists(path))
    {
        return loadContext.LoadFromAssemblyPath(path);
    }

    return null;
};

var assembly = loadContext.LoadFromAssemblyPath(tmlPath);
var mainType = assembly.GetType("Terraria.Main") ?? throw new InvalidOperationException("Terraria.Main not found");

Console.WriteLine("Fields containing 'menu':");
foreach (var field in mainType
             .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
             .Where(f => f.Name.Contains("menu", StringComparison.OrdinalIgnoreCase))
             .OrderBy(f => f.Name))
{
    Console.WriteLine($" - {field.FieldType.FullName} {field.Name}");
}

Console.WriteLine();
Console.WriteLine("Properties containing 'menu':");
foreach (var property in mainType
             .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
             .Where(p => p.Name.Contains("menu", StringComparison.OrdinalIgnoreCase))
             .OrderBy(p => p.Name))
{
    Console.WriteLine($" - {property.PropertyType.FullName} {property.Name}");
}

var shortcutsType = assembly.GetType("Terraria.UI.Gamepad.UILinkPointNavigator+Shortcuts");
if (shortcutsType is not null)
{
    Console.WriteLine();
    Console.WriteLine("UILinkPointNavigator shortcuts containing 'CRAFT':");
    foreach (var field in shortcutsType
                 .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                 .Where(f => f.FieldType == typeof(int) && f.Name.Contains("CRAFT", StringComparison.OrdinalIgnoreCase))
                 .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
    {
        if (field.GetValue(null) is int value)
        {
            Console.WriteLine($" - {field.Name} = {value}");
        }
    }
}

var linkPageIdType = assembly.GetType("Terraria.UI.Gamepad.UILinkPageID");
if (linkPageIdType is not null)
{
    Console.WriteLine();
    Console.WriteLine("UILinkPageID constants containing 'CRAFT':");
    foreach (var field in linkPageIdType
                 .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                 .Where(f => f.FieldType == typeof(int) && f.Name.Contains("CRAFT", StringComparison.OrdinalIgnoreCase))
                 .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
    {
        if (field.GetValue(null) is int value)
        {
            Console.WriteLine($" - {field.Name} = {value}");
        }
    }
}

Console.WriteLine();
Console.WriteLine("Fields containing 'recipe':");
foreach (var field in mainType
             .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
             .Where(f => f.Name.Contains("recipe", StringComparison.OrdinalIgnoreCase))
             .OrderBy(f => f.Name))
{
    Console.WriteLine($" - {field.FieldType.FullName} {field.Name}");
}

Console.WriteLine();
Console.WriteLine("Fields containing 'rec':");
foreach (var field in mainType
             .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
             .Where(f => f.Name.Contains("rec", StringComparison.OrdinalIgnoreCase))
             .OrderBy(f => f.Name))
{
    Console.WriteLine($" - {field.FieldType.FullName} {field.Name}");
}

var linkPointIdType = assembly.GetType("Terraria.UI.Gamepad.UILinkPointID");
if (linkPointIdType is not null)
{
    Console.WriteLine();
    Console.WriteLine("UILinkPointID constants containing 'CRAFT':");
    foreach (var field in linkPointIdType
                 .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                 .Where(f => f.FieldType == typeof(int) && f.Name.Contains("CRAFT", StringComparison.OrdinalIgnoreCase))
                 .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
    {
        if (field.GetValue(null) is int value)
        {
            Console.WriteLine($" - {field.Name} = {value}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("UILinkPointID constants containing 'RECIPE':");
    foreach (var field in linkPointIdType
                 .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                 .Where(f => f.FieldType == typeof(int) && f.Name.Contains("RECIPE", StringComparison.OrdinalIgnoreCase))
                 .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
    {
        if (field.GetValue(null) is int value)
        {
            Console.WriteLine($" - {field.Name} = {value}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("UILinkPointID constants containing 'CRAFTING':");
    foreach (var field in linkPointIdType
                 .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                 .Where(f => f.FieldType == typeof(int) && f.Name.Contains("CRAFTING", StringComparison.OrdinalIgnoreCase))
                 .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
    {
        if (field.GetValue(null) is int value)
        {
            Console.WriteLine($" - {field.Name} = {value}");
        }
    }
}

var pointsFieldInfo = assembly.GetType("Terraria.UI.Gamepad.UILinkPointNavigator")
    ?.GetField("Points", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
var pagesFieldInfo = assembly.GetType("Terraria.UI.Gamepad.UILinkPointNavigator")
    ?.GetField("Pages", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
if (pointsFieldInfo?.GetValue(null) is System.Collections.IDictionary pointsDictionary && pointsDictionary.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"UILinkPointNavigator has {pointsDictionary.Count} points loaded.");
    int printed = 0;
    foreach (System.Collections.DictionaryEntry entry in pointsDictionary)
    {
        var pointType = entry.Value?.GetType();
        if (pointType is null)
        {
            continue;
        }

        if (pointType.GetProperty("ID")?.GetValue(entry.Value) is int id &&
            pointType.GetProperty("PageID")?.GetValue(entry.Value) is int pageId)
        {
            string? positionString = pointType.GetProperty("Position", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(entry.Value)?.ToString();

            string? name = null;
            var displayNameProperty = pointType.GetProperty("DisplayName", BindingFlags.Public | BindingFlags.Instance);
            if (displayNameProperty is not null)
            {
                name = displayNameProperty.GetValue(entry.Value)?.ToString();
            }

            Console.WriteLine($" - Point {id} on page {pageId} at {positionString ?? "<unknown>"} display '{name ?? "<none>"}'");
        }

        printed++;
        if (printed >= 20)
        {
            Console.WriteLine(" ...");
            break;
        }
    }
}

if (pagesFieldInfo?.GetValue(null) is System.Collections.IDictionary pagesDictionary && pagesDictionary.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"UILinkPointNavigator has {pagesDictionary.Count} pages loaded.");
    int printed = 0;
    foreach (System.Collections.DictionaryEntry entry in pagesDictionary)
    {
        var pageType = entry.Value?.GetType();
        if (pageType is null)
        {
            continue;
        }

        string? name = pageType.GetField("Name", BindingFlags.Public | BindingFlags.Instance)?.GetValue(entry.Value)?.ToString();
        string? idString = entry.Key?.ToString();
        Console.WriteLine($" - Page {idString}: '{name ?? "<none>"}'");

        printed++;
        if (printed >= 20)
        {
            Console.WriteLine(" ...");
            break;
        }
    }
}

var itemSlotContextType = assembly.GetType("Terraria.UI.ItemSlot+Context");
if (itemSlotContextType is not null)
{
    Console.WriteLine();
    Console.WriteLine("ItemSlot.Context constants:");
    foreach (var field in itemSlotContextType
                 .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                 .Where(f => f.FieldType == typeof(int))
                 .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
    {
        if (field.GetValue(null) is int value)
        {
            Console.WriteLine($" - {field.Name} = {value}");
        }
    }
}

var uiLinkPointType = assembly.GetType("Terraria.UI.Gamepad.UILinkPoint");
if (uiLinkPointType is not null)
{
    Console.WriteLine();
    Console.WriteLine("UILinkPoint members:");
    foreach (var member in uiLinkPointType
                 .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                 .Where(m => m.MemberType is MemberTypes.Property or MemberTypes.Field or MemberTypes.Method)
                 .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
    {
        switch (member)
        {
            case PropertyInfo property:
                Console.WriteLine($" - Property {property.PropertyType.FullName} {property.Name}");
                break;
            case FieldInfo field:
                Console.WriteLine($" - Field {field.FieldType.FullName} {field.Name}");
                break;
            case MethodInfo method when method.IsSpecialName is false:
                Console.WriteLine($" - Method {method.Name}()");
                break;
        }
    }
}

var ingameOptionsType = assembly.GetType("Terraria.IngameOptions");
if (ingameOptionsType is not null)
{
    Console.WriteLine();
    Console.WriteLine("Terraria.IngameOptions fields:");
    foreach (var field in ingameOptionsType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
             .OrderBy(f => f.Name))
    {
        Console.WriteLine($" - {field.FieldType.FullName} {field.Name}");
    }

    Console.WriteLine();
    Console.WriteLine("Terraria.IngameOptions properties:");
    foreach (var property in ingameOptionsType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
             .OrderBy(p => p.Name))
    {
        Console.WriteLine($" - {property.PropertyType.FullName} {property.Name}");
    }
}

var langType = assembly.GetType("Terraria.Lang");
if (langType is not null)
{
    var languageManagerType = assembly.GetType("Terraria.Localization.LanguageManager");
    var gameCultureType = assembly.GetType("Terraria.Localization.GameCulture");
    var cultureNameType = gameCultureType?.GetNestedType("CultureName", BindingFlags.Public);

    if (languageManagerType is not null && gameCultureType is not null && cultureNameType is not null)
    {
        var instanceField = languageManagerType.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
        var instance = instanceField?.GetValue(null);
        var fromCultureName = gameCultureType.GetMethod("FromCultureName", BindingFlags.Public | BindingFlags.Static, binder: null, new[] { cultureNameType }, modifiers: null);
        var setLanguage = languageManagerType.GetMethod("SetLanguage", BindingFlags.Public | BindingFlags.Instance, binder: null, new[] { gameCultureType }, modifiers: null);

        if (instance is not null && fromCultureName is not null && setLanguage is not null)
        {
            var englishValue = Enum.Parse(cultureNameType, "English");
            var englishCulture = fromCultureName.Invoke(null, new[] { englishValue });
            setLanguage.Invoke(instance, new[] { englishCulture });
        }
    }

    langType.GetMethod("InitializeLegacyLocalization", BindingFlags.Public | BindingFlags.Static)
        ?.Invoke(null, null);

    var menuField = langType.GetField("menu", BindingFlags.Public | BindingFlags.Static);
    if (menuField?.GetValue(null) is Array menuArray)
    {
        int[] indicesToInspect =
        [
            49, 50, 51, 52, 59, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72,
            98, 99, 100, 101, 114, 118, 119, 121, 122, 123, 124,
            128, 129, 131, 132, 133, 147, 210, 213, 214, 215, 216, 217, 218,
            220, 221, 229, 230, 232, 233, 234, 241, 242, 247,
        ];

        Console.WriteLine();
        Console.WriteLine("Lang.menu entries used by IngameOptions:");
        foreach (int index in indicesToInspect.OrderBy(i => i))
        {
            if (index >= 0 && index < menuArray.Length &&
                menuArray.GetValue(index) is { } localizedText)
            {
                string value = localizedText.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)?.GetValue(localizedText)?.ToString() ?? "<null>";
                Console.WriteLine($" - [{index}] {value}");
            }
            else
            {
                Console.WriteLine($" - [{index}] <unavailable>");
            }
        }
    }
}
