#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace TerrariaAccess.Common.Systems.Journey;

internal static class JourneyPowersReflection
{
    private const BindingFlags InstanceAny = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Lazy<Type?> CreativePowerManagerType = new(() =>
        ResolveType("Terraria.GameContent.Creative.CreativePowerManager"));

    private static readonly Lazy<object?> CreativePowerManagerInstance = new(() =>
    {
        Type? t = CreativePowerManagerType.Value;
        if (t is null) return null;
        PropertyInfo? prop = t.GetProperty("Instance", StaticAny);
        return prop?.GetValue(null);
    });

    private static readonly Lazy<IDictionary?> PowersByName = new(() =>
    {
        object? mgr = CreativePowerManagerInstance.Value;
        if (mgr is null) return null;
        FieldInfo? field = CreativePowerManagerType.Value?.GetField("_powersByName", InstanceAny);
        return field?.GetValue(mgr) as IDictionary;
    });

    private static readonly Dictionary<Type, MethodInfo?> IsEnabledForPlayerCache = new();
    private static readonly Dictionary<Type, MethodInfo?> GetSliderValueCache = new();
    private static readonly Dictionary<Type, MethodInfo?> GetSliderValueForPlayerCache = new();

    public static object? TryGetPower(string key)
    {
        IDictionary? dict = PowersByName.Value;
        if (dict is null) return null;
        try
        {
            if (dict.Contains(key))
            {
                return dict[key];
            }
        }
        catch
        {
        }
        return null;
    }

    public static bool? TryGetTogglePerPlayerState(object power, int playerIndex)
    {
        MethodInfo? method = ResolveMethod(power.GetType(), IsEnabledForPlayerCache, "IsEnabledForPlayer", typeof(int));
        if (method is null) return null;
        try
        {
            object? result = method.Invoke(power, new object[] { playerIndex });
            return result as bool?;
        }
        catch
        {
            return null;
        }
    }

    public static float? TryGetSliderValue(object power, int playerIndex)
    {
        Type t = power.GetType();
        MethodInfo? perPlayer = ResolveMethod(t, GetSliderValueForPlayerCache, "GetRemappedSliderValueFor", typeof(int));
        if (perPlayer is not null)
        {
            try
            {
                object? result = perPlayer.Invoke(power, new object[] { playerIndex });
                if (result is float f1)
                {
                    return f1;
                }
            }
            catch
            {
            }
        }

        MethodInfo? generic = ResolveMethod(t, GetSliderValueCache, "GetSliderValue");
        if (generic is not null)
        {
            try
            {
                object? result = generic.Invoke(power, null);
                if (result is float f2)
                {
                    return f2;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static MethodInfo? ResolveMethod(Type t, Dictionary<Type, MethodInfo?> cache, string name, params Type[] paramTypes)
    {
        if (cache.TryGetValue(t, out MethodInfo? cached))
        {
            return cached;
        }

        MethodInfo? method = null;
        Type? cursor = t;
        while (cursor is not null && method is null)
        {
            method = paramTypes is null || paramTypes.Length == 0
                ? cursor.GetMethod(name, InstanceAny, binder: null, types: Type.EmptyTypes, modifiers: null)
                : cursor.GetMethod(name, InstanceAny, binder: null, types: paramTypes, modifiers: null);
            if (method is null)
            {
                method = cursor.GetMethod(name, InstanceAny);
            }
            cursor = cursor.BaseType;
        }

        cache[t] = method;
        return method;
    }

    private static Type? ResolveType(string fullName)
    {
        Type? found = Type.GetType(fullName);
        if (found is not null) return found;

        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? t = asm.GetType(fullName, throwOnError: false, ignoreCase: false);
                if (t is not null) return t;
            }
            catch
            {
            }
        }

        return null;
    }
}
