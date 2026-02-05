#nullable enable

namespace ScreenReaderMod.Common.Systems.Inventory;

/// <summary>
/// Types of narration cues for deduplication.
/// </summary>
internal enum NarrationKind
{
    MouseItem,
    HoverItem,
    EmptySlot,
    Tooltip,
    UiHover,
    SpecialSelection,
    Count,
}

/// <summary>
/// Represents a narration announcement for deduplication purposes.
/// </summary>
internal readonly record struct NarrationCue(
    NarrationKind Kind,
    string Message,
    ItemIdentity Identity,
    string? Location,
    string? Tooltip,
    string? Details,
    int SlotSignature)
{
    public static NarrationCue ForMouse(ItemIdentity identity, string message)
    {
        return new NarrationCue(NarrationKind.MouseItem, message, identity, null, null, null, -1);
    }

    public static NarrationCue ForItem(ItemIdentity identity, string message, string? location, string? tooltip, string? details, int slotSignature)
    {
        return new NarrationCue(NarrationKind.HoverItem, message, identity, location, tooltip, details, slotSignature);
    }

    public static NarrationCue ForEmpty(string message, string location, int slotSignature)
    {
        return new NarrationCue(NarrationKind.EmptySlot, message, ItemIdentity.Empty, location, null, null, slotSignature);
    }

    public static NarrationCue ForTooltip(string message)
    {
        return new NarrationCue(NarrationKind.Tooltip, message, ItemIdentity.Empty, null, message, null, -1);
    }

    public static NarrationCue ForUi(string message)
    {
        return new NarrationCue(NarrationKind.UiHover, message, ItemIdentity.Empty, null, message, null, -1);
    }

    public static NarrationCue ForSpecial(string label)
    {
        return new NarrationCue(NarrationKind.SpecialSelection, label, ItemIdentity.Empty, null, label, null, -1);
    }
}
