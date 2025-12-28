#nullable enable
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.Localization;

namespace ScreenReaderMod.Common.Systems;

internal static class TileStateDescriptorService
{
    private static readonly Dictionary<byte, string> PaintColorKeys = BuildPaintColorKeyMap();

    public static List<string> GetStateChanges(
        BlockType oldBlockType, bool oldIsActuated, bool oldHasActuator,
        bool oldRedWire, bool oldGreenWire, bool oldBlueWire, bool oldYellowWire,
        byte oldTileColor, byte oldWallColor,
        bool oldIsTileInvisible, bool oldIsWallInvisible,
        bool oldIsTileFullbright, bool oldIsWallFullbright,
        BlockType newBlockType, bool newIsActuated, bool newHasActuator,
        bool newRedWire, bool newGreenWire, bool newBlueWire, bool newYellowWire,
        byte newTileColor, byte newWallColor,
        bool newIsTileInvisible, bool newIsWallInvisible,
        bool newIsTileFullbright, bool newIsWallFullbright)
    {
        List<string> changes = new();

        // Shape changes (highest priority)
        if (oldBlockType != newBlockType)
        {
            string? shapeDesc = GetBlockTypeDescriptor(newBlockType);
            if (!string.IsNullOrEmpty(shapeDesc))
                changes.Add(shapeDesc);
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

        // Wire changes
        AddWireChange(changes, oldRedWire, newRedWire, "Red");
        AddWireChange(changes, oldGreenWire, newGreenWire, "Green");
        AddWireChange(changes, oldBlueWire, newBlueWire, "Blue");
        AddWireChange(changes, oldYellowWire, newYellowWire, "Yellow");

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

    private static void AddWireChange(List<string> changes, bool oldVal, bool newVal, string color)
    {
        if (oldVal == newVal) return;

        string key = newVal
            ? $"TileStates.{color}WireAdded"
            : $"TileStates.{color}WireRemoved";
        changes.Add(GetLocalized(key));
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

    private static string GetLocalized(string key)
    {
        string fullKey = $"Mods.ScreenReaderMod.{key}";
        string value = Language.GetTextValue(fullKey);
        if (string.Equals(value, fullKey, StringComparison.Ordinal))
        {
            // Fallback to the key suffix as a readable name
            int lastDot = key.LastIndexOf('.');
            return lastDot >= 0 ? key[(lastDot + 1)..] : key;
        }
        return value;
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
