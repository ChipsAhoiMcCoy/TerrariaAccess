#nullable enable
using TerrariaAccess.Common.Abstractions;

namespace TerrariaAccess.Common.Adapters;

/// <summary>
/// Production adapter that wraps Terraria's localization system.
/// </summary>
internal sealed class TerrariaLocalizationAdapter : ITerrariaLocalization
{
    public string GetCoinLabel(int index) => Terraria.Lang.inter[index].Value;
}
