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
