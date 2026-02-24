#nullable enable
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.Localization;

namespace TerrariaAccess.Common.Systems;

internal static class TileStateDescriptorService
{
    private static readonly Dictionary<byte, string> PaintColorKeys = BuildPaintColorKeyMap();

    public static List<string> GetStateChanges(
        BlockType oldBlockType, bool oldIsActuated, bool oldHasActuator,
        bool oldRedWire, bool oldGreenWire, bool oldBlueWire, bool oldYellowWire,
        byte oldTileColor, byte oldWallColor,
        bool oldIsTileInvisible, bool oldIsWallInvisible,
        bool oldIsTileFullbright, bool oldIsWallFullbright,
        bool oldIsToggleableTile, bool oldIsToggleableOn,
        BlockType newBlockType, bool newIsActuated, bool newHasActuator,
        bool newRedWire, bool newGreenWire, bool newBlueWire, bool newYellowWire,
        byte newTileColor, byte newWallColor,
        bool newIsTileInvisible, bool newIsWallInvisible,
        bool newIsTileFullbright, bool newIsWallFullbright,
        bool newIsToggleableTile, bool newIsToggleableOn,
        int oldJunctionBoxMode = -1, int newJunctionBoxMode = -1)
    {
        List<string> changes = new();

        // Shape changes (highest priority)
        if (oldBlockType != newBlockType)
        {
            string? shapeDesc = GetBlockTypeDescriptor(newBlockType);
            if (!string.IsNullOrEmpty(shapeDesc))
                changes.Add(shapeDesc);
        }

        // Lever toggle state changes (switches are excluded - they're simple buttons)
        if (newIsToggleableTile && oldIsToggleableOn != newIsToggleableOn)
        {
            changes.Add(newIsToggleableOn ? "on" : "off");
        }

        // Junction Box mode changes
        if (newJunctionBoxMode >= 0 && oldJunctionBoxMode != newJunctionBoxMode)
        {
            string? modeLabel = GetJunctionBoxModeLabel(newJunctionBoxMode);
            if (!string.IsNullOrEmpty(modeLabel))
            {
                changes.Add(modeLabel);
            }
        }

        // Actuator state
        if (oldIsActuated != newIsActuated)
        {
            changes.Add(GetLocalized(newIsActuated
                ? "TileStates.Actuated" : "TileStates.Unactuated"));
        }

        if (oldHasActuator != newHasActuator)
        {
            changes.Add(GetLocalized(newHasActuator
                ? "TileStates.ActuatorPlaced" : "TileStates.ActuatorRemoved"));
        }

        // Wire changes - collect added/removed separately for brief format
        List<string> addedWireColors = new();
        List<string> removedWireColors = new();
        CollectWireChange(addedWireColors, removedWireColors, oldRedWire, newRedWire, "Red");
        CollectWireChange(addedWireColors, removedWireColors, oldGreenWire, newGreenWire, "Green");
        CollectWireChange(addedWireColors, removedWireColors, oldBlueWire, newBlueWire, "Blue");
        CollectWireChange(addedWireColors, removedWireColors, oldYellowWire, newYellowWire, "Yellow");

        // Format added wires as brief: "Red wire" or "Red, Blue wire"
        if (addedWireColors.Count > 0)
        {
            changes.Add($"{string.Join(", ", addedWireColors)} wire");
        }

        // Format removed wires individually: "red wire removed"
        foreach (string color in removedWireColors)
        {
            changes.Add(GetLocalized($"TileStates.{color}WireRemoved"));
        }

        // Paint changes
        if (oldTileColor != newTileColor)
        {
            AddPaintChange(changes, newTileColor, "TileStates.PaintedFormat",
                "TileStates.PaintRemoved");
        }

        if (oldWallColor != newWallColor)
        {
            AddPaintChange(changes, newWallColor, "TileStates.WallPaintedFormat",
                "TileStates.WallPaintRemoved");
        }

        // Coating changes
        AddBooleanChange(changes, oldIsTileInvisible, newIsTileInvisible,
            "TileStates.EchoCoatingApplied", "TileStates.EchoCoatingRemoved");
        AddBooleanChange(changes, oldIsWallInvisible, newIsWallInvisible,
            "TileStates.WallEchoCoatingApplied", "TileStates.WallEchoCoatingRemoved");
        AddBooleanChange(changes, oldIsTileFullbright, newIsTileFullbright,
            "TileStates.GlowPaintApplied", "TileStates.GlowPaintRemoved");
        AddBooleanChange(changes, oldIsWallFullbright, newIsWallFullbright,
            "TileStates.WallGlowPaintApplied", "TileStates.WallGlowPaintRemoved");

        return changes;
    }

    private static void CollectWireChange(List<string> added, List<string> removed, bool oldVal, bool newVal, string color)
    {
        if (oldVal == newVal) return;

        if (newVal)
            added.Add(GetWireColorName(color));
        else
            removed.Add(color);
    }

    private static void AddPaintChange(List<string> changes, byte paintId,
        string formatKey, string removedKey)
    {
        if (paintId == 0)
        {
            changes.Add(GetLocalized(removedKey));
        }
        else
        {
            string? colorName = GetPaintColorName(paintId);
            if (!string.IsNullOrEmpty(colorName))
            {
                string format = GetLocalized(formatKey);
                changes.Add(string.Format(format, colorName));
            }
        }
    }

    private static void AddBooleanChange(List<string> changes, bool oldVal, bool newVal,
        string addedKey, string removedKey)
    {
        if (oldVal == newVal) return;
        changes.Add(GetLocalized(newVal ? addedKey : removedKey));
    }

    public static string? GetPaintColorName(byte paintId)
    {
        if (paintId == 0) return null;

        if (PaintColorKeys.TryGetValue(paintId, out string? key))
        {
            return GetLocalized(key);
        }

        // Fallback for unknown paint IDs
        return $"paint {paintId}";
    }

    public static string? GetBlockTypeDescriptor(BlockType blockType)
    {
        return blockType switch
        {
            BlockType.HalfBlock => GetLocalized("TileShapes.HalfBlock"),
            BlockType.SlopeDownLeft => GetLocalized("TileShapes.SlopeDownLeft"),
            BlockType.SlopeDownRight => GetLocalized("TileShapes.SlopeDownRight"),
            BlockType.SlopeUpLeft => GetLocalized("TileShapes.SlopeUpLeft"),
            BlockType.SlopeUpRight => GetLocalized("TileShapes.SlopeUpRight"),
            BlockType.Solid => GetLocalized("TileShapes.Solid"),
            _ => null,
        };
    }

    /// <summary>
    /// Formats existing actuator on a tile as "actuator on" or "actuator off".
    /// Returns null if no actuator exists.
    /// </summary>
    /// <param name="hasActuator">Whether the tile has an actuator placed.</param>
    /// <param name="isActuated">Whether the tile is currently actuated (inactive/phased).</param>
    public static string? FormatExistingActuator(bool hasActuator, bool isActuated)
    {
        if (!hasActuator) return null;
        // "actuator on" = tile is actuated (inactive/passable)
        // "actuator off" = tile is solid
        return GetLocalized(isActuated ? "TileStates.ActuatorOn" : "TileStates.ActuatorOff");
    }

    /// <summary>
    /// Formats existing wires on a tile as "Red wire" or "Red, Blue wire".
    /// Returns null if no wires exist.
    /// </summary>
    public static string? FormatExistingWires(bool redWire, bool greenWire, bool blueWire, bool yellowWire)
    {
        List<string> colors = new();
        if (redWire) colors.Add(GetWireColorName("Red"));
        if (greenWire) colors.Add(GetWireColorName("Green"));
        if (blueWire) colors.Add(GetWireColorName("Blue"));
        if (yellowWire) colors.Add(GetWireColorName("Yellow"));

        if (colors.Count == 0) return null;

        return $"{string.Join(", ", colors)} wire";
    }

    private static string GetWireColorName(string color)
    {
        string localized = GetLocalized($"WireColors.{color}");
        // Fallback if localization returns the key suffix
        if (localized == color || localized.Contains("WireColors"))
            return color;
        return localized;
    }

    private static string GetLocalized(string key)
    {
        string fullKey = $"Mods.TerrariaAccess.{key}";
        string value = Language.GetTextValue(fullKey);
        if (string.Equals(value, fullKey, StringComparison.Ordinal))
        {
            // Fallback to the key suffix as a readable name
            int lastDot = key.LastIndexOf('.');
            return lastDot >= 0 ? key[(lastDot + 1)..] : key;
        }
        return value;
    }

    /// <summary>
    /// Gets the mode description for Junction Box mode changes.
    /// </summary>
    private static string? GetJunctionBoxModeLabel(int mode)
    {
        return mode switch
        {
            0 => "wires pass through in the same direction",
            1 => "down connects to left, up connects to right",
            2 => "down connects to right, up connects to left",
            _ => null,
        };
    }

    private static Dictionary<byte, string> BuildPaintColorKeyMap()
    {
        return new Dictionary<byte, string>
        {
            [PaintID.RedPaint] = "PaintColors.Red",
            [PaintID.OrangePaint] = "PaintColors.Orange",
            [PaintID.YellowPaint] = "PaintColors.Yellow",
            [PaintID.LimePaint] = "PaintColors.Lime",
            [PaintID.GreenPaint] = "PaintColors.Green",
            [PaintID.TealPaint] = "PaintColors.Teal",
            [PaintID.CyanPaint] = "PaintColors.Cyan",
            [PaintID.SkyBluePaint] = "PaintColors.SkyBlue",
            [PaintID.BluePaint] = "PaintColors.Blue",
            [PaintID.PurplePaint] = "PaintColors.Purple",
            [PaintID.VioletPaint] = "PaintColors.Violet",
            [PaintID.PinkPaint] = "PaintColors.Pink",
            [PaintID.DeepRedPaint] = "PaintColors.DeepRed",
            [PaintID.DeepOrangePaint] = "PaintColors.DeepOrange",
            [PaintID.DeepYellowPaint] = "PaintColors.DeepYellow",
            [PaintID.DeepLimePaint] = "PaintColors.DeepLime",
            [PaintID.DeepGreenPaint] = "PaintColors.DeepGreen",
            [PaintID.DeepTealPaint] = "PaintColors.DeepTeal",
            [PaintID.DeepCyanPaint] = "PaintColors.DeepCyan",
            [PaintID.DeepSkyBluePaint] = "PaintColors.DeepSkyBlue",
            [PaintID.DeepBluePaint] = "PaintColors.DeepBlue",
            [PaintID.DeepPurplePaint] = "PaintColors.DeepPurple",
            [PaintID.DeepVioletPaint] = "PaintColors.DeepViolet",
            [PaintID.DeepPinkPaint] = "PaintColors.DeepPink",
            [PaintID.BlackPaint] = "PaintColors.Black",
            [PaintID.WhitePaint] = "PaintColors.White",
            [PaintID.GrayPaint] = "PaintColors.Gray",
            [PaintID.BrownPaint] = "PaintColors.Brown",
            [PaintID.ShadowPaint] = "PaintColors.Shadow",
            [PaintID.NegativePaint] = "PaintColors.Negative",
            [PaintID.IlluminantPaint] = "PaintColors.Illuminant",
        };
    }
}
