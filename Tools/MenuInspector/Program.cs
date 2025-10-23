using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

var baseDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var tmlDir = Path.Combine(baseDir, "tModLoader_dist");
var tmlPath = Path.Combine(tmlDir, "tModLoader.dll");

var assembly = new AssemblyLoadContext("tml", isCollectible: false).LoadFromAssemblyPath(tmlPath);

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
