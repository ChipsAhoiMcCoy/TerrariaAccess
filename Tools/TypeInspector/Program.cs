using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

string baseDir = @"C:\\Program Files (x86)\\Steam\\steamapps\\common\\tModLoader";
string asmPath = Path.Combine(baseDir, "tModLoader.dll");
var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
void AddAssemblies(string directory)
{
    if (!Directory.Exists(directory))
    {
        return;
    }

    foreach (string file in Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories))
    {
        lookup.TryAdd(Path.GetFileName(file), file);
    }
}

AddAssemblies(baseDir);
AddAssemblies(Path.Combine(baseDir, "Libraries"));
AddAssemblies(Path.Combine(baseDir, "dotnet", "shared"));
var context = new AssemblyLoadContext("TypeInspector", isCollectible: true);
context.Resolving += (_, name) => lookup.TryGetValue(name.Name + ".dll", out string path) && File.Exists(path)
    ? context.LoadFromAssemblyPath(path)
    : null;

Assembly asm = context.LoadFromAssemblyPath(asmPath);
Type shortcutsType = asm.GetType("Terraria.UI.Gamepad.UILinkPointNavigator+Shortcuts")!;
object shortcuts = shortcutsType.GetField("Empty", BindingFlags.Static | BindingFlags.Public)?.GetValue(null) ?? Activator.CreateInstance(shortcutsType)!;
foreach (FieldInfo field in shortcutsType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
{
    object? value = field.GetValue(shortcutsType);
    Console.Write($"{field.FieldType.FullName} {field.Name}");
    if (value is Array array)
    {
        Console.Write(" => [" + string.Join(",", array.Cast<object>()) + "]");
    }
    Console.WriteLine();
}

Console.WriteLine("--- UIVirtualKeyboard Members ---");
Type keyboardType = asm.GetType("Terraria.GameContent.UI.States.UIVirtualKeyboard")!;
foreach (MemberInfo member in keyboardType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
{
    Console.WriteLine($"{member.MemberType}: {member}");
}
