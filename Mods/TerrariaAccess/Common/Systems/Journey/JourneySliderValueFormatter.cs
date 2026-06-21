#nullable enable
using System;
using System.Globalization;
using TerrariaAccess.Common.Utilities;

namespace TerrariaAccess.Common.Systems.Journey;

internal static class JourneySliderValueFormatter
{
    public static string Format(string powerKey, float sliderValue01)
    {
        float clamped = sliderValue01 < 0f ? 0f : (sliderValue01 > 1f ? 1f : sliderValue01);
        return powerKey switch
        {
            "time_setspeed" => FormatTimes(MapTimeRate(clamped)),
            "setdifficulty" => FormatTimes(MapDifficulty(clamped), "0.##"),
            "setspawnrate" => FormatPercent(clamped * 100f),
            "wind_setstrength" => FormatSignedPercent((clamped * 2f - 1f) * 100f),
            "rain_setstrength" => FormatPercent(clamped * 100f),
            _ => FormatPercent(clamped * 100f),
        };
    }

    public static int QuantizeForChangeDetection(string powerKey, float sliderValue01)
    {
        float clamped = sliderValue01 < 0f ? 0f : (sliderValue01 > 1f ? 1f : sliderValue01);
        return powerKey switch
        {
            "time_setspeed" => (int)(MapTimeRate(clamped) * 10f),
            "setdifficulty" => (int)Math.Round(MapDifficulty(clamped) * 20f),
            _ => (int)(clamped * 100f),
        };
    }

    private static float MapTimeRate(float v)
    {
        return 1f + v * 23f;
    }

    private static float MapDifficulty(float v)
    {
        float mapped = v <= 0.33f
            ? 0.5f + (v / 0.33f) * 0.5f
            : 1f + ((v - 0.33f) / 0.67f) * 2f;

        return (float)Math.Round(mapped * 20f) / 20f;
    }

    private static string FormatTimes(float value, string format = "0.#")
    {
        string num = value.ToString(format, CultureInfo.InvariantCulture);
        return string.Format(
            LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.JourneyMode.SliderUnit.TimesFormat",
                "{0} times"),
            num);
    }

    private static string FormatPercent(float value)
    {
        string num = value.ToString("0", CultureInfo.InvariantCulture);
        return string.Format(
            LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.JourneyMode.SliderUnit.PercentFormat",
                "{0} percent"),
            num);
    }

    private static string FormatSignedPercent(float value)
    {
        string num = value.ToString("+0;-0;0", CultureInfo.InvariantCulture);
        return string.Format(
            LocalizationHelper.GetTextOrFallback(
                "Mods.TerrariaAccess.JourneyMode.SliderUnit.SignedPercentFormat",
                "{0} percent"),
            num);
    }
}
