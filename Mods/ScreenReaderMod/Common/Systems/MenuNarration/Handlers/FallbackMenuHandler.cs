#nullable enable
using System;
using System.Collections.Generic;
using ScreenReaderMod.Common.Utilities;
using Terraria;
using Terraria.ID;
using Terraria.UI;

namespace ScreenReaderMod.Common.Systems.MenuNarration.Handlers;

/// <summary>
/// Fallback handler for menus that don't have a specialized handler.
/// Provides basic hover and focus narration.
/// </summary>
internal sealed class FallbackMenuHandler : MenuHandlerBase
{
    public override int Priority => 0; // Lowest priority - used as fallback

    public override bool CanHandle(int menuMode, UIState? uiState)
    {
        // Always return true - this is the fallback handler
        return true;
    }

    public override IEnumerable<MenuNarrationEvent> Update(MenuNarrationContext context)
    {
        var events = new List<MenuNarrationEvent>();

        if (!context.IsMenuActive)
        {
            return events;
        }

        int currentMode = context.MenuMode;
        DateTime now = context.Timestamp;

        if (ModeJustEntered)
        {
            HandleMenuModeChanged(context, now, events);
            ModeJustEntered = false;
            return events;
        }

        // Suppress if accessibility systems are handling gamepad input
        if (IsAccessibilitySystemHandling())
        {
            return events;
        }

        // Handle hover events
        if (TryHandleUiHover(context, events))
        {
            return events;
        }

        // Handle focus changes
        if (!TryHandleFocus(context, false, events))
        {
            AnnounceFallbackSafe(context, events);
        }

        return events;
    }

    private void HandleMenuModeChanged(MenuNarrationContext context, DateTime timestamp, List<MenuNarrationEvent> events)
    {
        string modeLabel = MenuNarrationCatalog.DescribeMenuMode(context.MenuMode, context.UiState);
        State.LastModeAnnouncement = modeLabel;
        State.LastModeAnnouncedAt = timestamp;

        MenuNarrationCatalog.LogMenuSnapshot(context.MenuMode);

        if (context.UiState is not null)
        {
            ScreenReaderMod.Instance?.Logger.Info($"[FallbackHandler] UI state: {context.UiState.GetType().FullName}");
        }

        // Suppress if accessibility systems are handling gamepad input
        if (IsAccessibilitySystemHandling())
        {
            return;
        }

        // Handle hover
        if (TryHandleUiHover(context, events))
        {
            return;
        }

        // Force focus announcement
        if (!TryHandleFocus(context, true, events))
        {
            AnnounceFallbackSafe(context, events);
        }
    }

    private static bool IsAccessibilitySystemHandling()
    {
        // Check if any accessibility system is currently handling input
        return Systems.WorkshopHubAccessibilitySystem.IsHandlingGamepadInput ||
               Systems.ManageModsAccessibilitySystem.IsHandlingGamepadInput ||
               Systems.ModInfoAccessibilitySystem.IsHandlingGamepadInput ||
               Systems.DownloadModsAccessibilitySystem.IsHandlingGamepadInput ||
               Systems.AchievementsMenuGamepadSystem.IsHandlingGamepadInput;
    }

    private void AnnounceFallbackSafe(MenuNarrationContext context, List<MenuNarrationEvent> events)
    {
        int currentMode = context.MenuMode;

        // Don't announce fallback for title menu - it has its own handler
        if (currentMode == MenuID.Title)
        {
            return;
        }

        // Don't announce fallback if accessibility systems are handling input
        if (IsAccessibilitySystemHandling())
        {
            return;
        }

        AnnounceFallback(context, events);
    }

    protected override bool IsAllowedHover(int menuMode, string cleanedLabel)
    {
        if (!base.IsAllowedHover(menuMode, cleanedLabel))
        {
            return false;
        }

        // Additional suppression checks
        if (IsAccessibilitySystemHandling())
        {
            return false;
        }

        return true;
    }
}
