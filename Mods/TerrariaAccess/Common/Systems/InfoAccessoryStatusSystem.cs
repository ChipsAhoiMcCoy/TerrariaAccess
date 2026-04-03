#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Utilities;
using Terraria;
using Terraria.ModLoader;

namespace TerrariaAccess.Common.Systems;

internal static class InfoAccessoryStatusSystem
{
    private const int CycleCooldownFrames = 90; // ~1.5 seconds at 60fps

    private static int _lastPressFrame;
    private static int _cycleIndex;

    private static readonly InfoDisplayDefinition[] OrderedDisplays =
    {
        new(InfoDisplay.Watches, "Time"),
        new(InfoDisplay.Compass, "Compass"),
        new(InfoDisplay.DepthMeter, "Depth Meter"),
        new(InfoDisplay.Radar, "Radar"),
        new(InfoDisplay.TallyCounter, "Tally Counter"),
        new(InfoDisplay.LifeformAnalyzer, "Lifeform Analyzer"),
        new(InfoDisplay.Stopwatch, "Stopwatch"),
        new(InfoDisplay.DPSMeter, "DPS Meter"),
        new(InfoDisplay.MetalDetector, "Metal Detector"),
        new(InfoDisplay.FishFinder, "Fishing Power"),
        new(InfoDisplay.WeatherRadio, "Weather"),
        new(InfoDisplay.Sextant, "Moon Phase"),
    };

    internal static void Announce(Player player)
    {
        List<string> announcements = BuildAnnouncements(player);
        if (announcements.Count == 0)
        {
            _cycleIndex = 0;
            _lastPressFrame = (int)Main.GameUpdateCount;
            ScreenReaderService.Announce("No informational accessories active", force: true);
            return;
        }

        int currentFrame = (int)Main.GameUpdateCount;
        int framesSinceLastPress = currentFrame - _lastPressFrame;

        if (framesSinceLastPress <= CycleCooldownFrames && _lastPressFrame > 0)
        {
            _cycleIndex = (_cycleIndex + 1) % announcements.Count;
            ScreenReaderService.Announce(announcements[_cycleIndex], force: true);
        }
        else
        {
            _cycleIndex = 0;
            ScreenReaderService.Announce(string.Join(". ", announcements) + ".", force: true);
        }

        _lastPressFrame = currentFrame;
    }

    private static List<string> BuildAnnouncements(Player player)
    {
        List<string> announcements = new();

        foreach (InfoDisplayDefinition definition in OrderedDisplays)
        {
            string? announcement = TryBuildInfoDisplayAnnouncement(definition);
            if (!string.IsNullOrWhiteSpace(announcement))
            {
                announcements.Add(announcement);
            }
        }

        string? mechanicalLens = TryBuildMechanicalLensAnnouncement(player);
        if (!string.IsNullOrWhiteSpace(mechanicalLens))
        {
            announcements.Add(mechanicalLens);
        }

        string? mechanicalRuler = TryBuildMechanicalRulerAnnouncement();
        if (!string.IsNullOrWhiteSpace(mechanicalRuler))
        {
            announcements.Add(mechanicalRuler);
        }

        return announcements;
    }

    private static string? TryBuildInfoDisplayAnnouncement(InfoDisplayDefinition definition)
    {
        if (!definition.Display.Active())
        {
            return null;
        }

        Color displayColor = default;
        Color shadowColor = default;
        string value = TextSanitizer.Clean(definition.Display.DisplayValue(ref displayColor, ref shadowColor));
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string label = GetDisplayLabel(definition);
        return $"{label}: {value}";
    }

    private static string GetDisplayLabel(InfoDisplayDefinition definition)
    {
        string label = TextSanitizer.Clean(definition.Display.DisplayName?.Value);
        return string.IsNullOrWhiteSpace(label) ? definition.FallbackLabel : label;
    }

    private static string? TryBuildMechanicalLensAnnouncement(Player player)
    {
        if (!player.InfoAccMechShowWires)
        {
            return null;
        }

        string mode = TextSanitizer.Clean(BuilderToggle.HideAllWires.DisplayValue());
        if (string.IsNullOrWhiteSpace(mode))
        {
            return "Mechanical Lens active";
        }

        return $"Mechanical Lens: {mode}";
    }

    private static string? TryBuildMechanicalRulerAnnouncement()
    {
        if (!BuilderToggle.RulerGrid.Active())
        {
            return null;
        }

        string state = TextSanitizer.Clean(BuilderToggle.RulerGrid.DisplayValue());
        if (string.IsNullOrWhiteSpace(state))
        {
            return "Mechanical Ruler active";
        }

        return $"Mechanical Ruler: {state}";
    }

    private sealed record InfoDisplayDefinition(InfoDisplay Display, string FallbackLabel);
}
