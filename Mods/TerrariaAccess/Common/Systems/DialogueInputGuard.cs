#nullable enable
using System;
using Terraria;
using Terraria.GameInput;
using Terraria.UI.Gamepad;

namespace TerrariaAccess.Common.Systems;

internal static class DialogueInputGuard
{
    private static readonly bool DebugEnabled =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SRM_DEBUG_DIALOGUE_INPUT")) ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SRM_DEBUG_INPUT"));

    private static string? _lastStateKey;

    internal static bool IsDialogueUiActive(Player? player = null)
    {
        player ??= Main.LocalPlayer;
        return player is not null && player.active && (player.talkNPC != -1 || player.sign != -1);
    }

    internal static void ClaimUiInput(Player player, string context)
    {
        if (player is null || !player.active)
        {
            return;
        }

        if (HasGameplayLeakState(player))
        {
            LogState(context, player, "pre-claim");
        }

        player.mouseInterface = true;
        player.releaseUseItem = false;
        player.controlUseItem = false;
        player.controlUseTile = false;
        player.controlHook = false;
        player.controlMount = false;

        TriggersPack pack = PlayerInput.Triggers;
        pack.Current.Grapple = false;
        pack.JustPressed.Grapple = false;
        pack.Current.QuickMount = false;
        pack.JustPressed.QuickMount = false;

        LogStateIfChanged(context, player, "claimed");
    }

    internal static void SuppressGameplayTriggersEarly(Player player, TriggersSet triggersSet, string context)
    {
        if (player is null || !player.active || triggersSet is null)
        {
            return;
        }

        bool hadLeakState = triggersSet.Grapple ||
                            triggersSet.QuickMount ||
                            PlayerInput.Triggers.Current.Grapple ||
                            PlayerInput.Triggers.Current.QuickMount ||
                            player.controlHook ||
                            player.controlMount ||
                            player.controlUseTile ||
                            player.controlUseItem;

        if (hadLeakState)
        {
            LogState(context, player, "early-suppress-pre");
        }

        triggersSet.Grapple = false;
        triggersSet.QuickMount = false;
        player.controlHook = false;
        player.controlMount = false;
        player.controlUseItem = false;
        player.controlUseTile = false;

        TriggersPack pack = PlayerInput.Triggers;
        pack.Current.Grapple = false;
        pack.JustPressed.Grapple = false;
        pack.Current.QuickMount = false;
        pack.JustPressed.QuickMount = false;

        if (hadLeakState)
        {
            LogStateIfChanged(context, player, "early-suppress-claimed");
        }
    }

    internal static void LogState(string context, Player player, string reason)
    {
        if (!DebugEnabled)
        {
            return;
        }

        LogCore(context, player, reason, dedupe: false);
    }

    internal static void LogStateIfChanged(string context, Player player, string reason)
    {
        if (!DebugEnabled)
        {
            return;
        }

        LogCore(context, player, reason, dedupe: true);
    }

    private static bool HasGameplayLeakState(Player player)
    {
        TriggersPack pack = PlayerInput.Triggers;
        return Main.mouseLeft ||
               Main.mouseRight ||
               pack.Current.MouseLeft ||
               pack.Current.MouseRight ||
               pack.Current.Grapple ||
               pack.Current.QuickMount ||
               player.controlUseItem ||
               player.controlUseTile ||
               player.controlHook ||
               player.controlMount;
    }

    private static void LogCore(string context, Player player, string reason, bool dedupe)
    {
        string stateKey = BuildStateKey(player);
        if (dedupe && string.Equals(stateKey, _lastStateKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastStateKey = stateKey;
        global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Info(BuildMessage(context, player, reason));
    }

    private static string BuildStateKey(Player player)
    {
        TriggersPack pack = PlayerInput.Triggers;
        Item heldItem = player.inventory[player.selectedItem];

        return string.Join("|",
            PlayerInput.CurrentInputMode,
            UILinkPointNavigator.CurrentPoint,
            Main.playerInventory ? 1 : 0,
            Main.editSign ? 1 : 0,
            player.talkNPC,
            player.sign,
            player.mouseInterface ? 1 : 0,
            Main.mouseLeft ? 1 : 0,
            pack.Current.MouseLeft ? 1 : 0,
            pack.Current.Grapple ? 1 : 0,
            pack.Current.QuickMount ? 1 : 0,
            player.controlUseItem ? 1 : 0,
            player.controlUseTile ? 1 : 0,
            player.controlHook ? 1 : 0,
            player.controlMount ? 1 : 0,
            heldItem.type,
            heldItem.Name);
    }

    private static string BuildMessage(string context, Player player, string reason)
    {
        TriggersPack pack = PlayerInput.Triggers;
        Item heldItem = player.inventory[player.selectedItem];

        return $"[DialogueInputDebug] {context}: " +
               $"reason={reason} " +
               $"frame={Main.GameUpdateCount} " +
               $"inputMode={PlayerInput.CurrentInputMode} " +
               $"linkPoint={UILinkPointNavigator.CurrentPoint} " +
               $"inventory={Main.playerInventory} " +
               $"editSign={Main.editSign} " +
               $"talkNPC={player.talkNPC} " +
               $"sign={player.sign} " +
               $"mouseInterface={player.mouseInterface} " +
               $"mainMouseLeft={Main.mouseLeft} " +
               $"triggerMouseLeft={pack.Current.MouseLeft} " +
               $"triggerGrapple={pack.Current.Grapple} " +
               $"triggerQuickMount={pack.Current.QuickMount} " +
               $"controlUseItem={player.controlUseItem} " +
               $"controlUseTile={player.controlUseTile} " +
               $"controlHook={player.controlHook} " +
               $"controlMount={player.controlMount} " +
               $"heldItemType={heldItem.type} " +
               $"heldItem='{heldItem.Name}'";
    }
}
