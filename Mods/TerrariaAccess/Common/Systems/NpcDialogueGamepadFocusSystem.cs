#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI.Gamepad;

namespace TerrariaAccess.Common.Systems;

/// <summary>
/// Keeps sign and NPC dialogue buttons aligned with the active gamepad link point.
/// Vanilla relies on virtual mouse movement for these buttons, which can leave
/// sign/grave marker actions unreachable when the cursor does not follow focus.
/// </summary>
public sealed class NpcDialogueGamepadFocusSystem : ModSystem
{
    private const int PrimaryPointId = 2500;
    private const int ClosePointId = 2501;
    private const int SecondaryPointId = 2502;
    private const int HappinessPointId = 2503;
    private const float ThumbstickThreshold = 0.55f;

    public override void Load()
    {
        if (Main.dedServ)
        {
            return;
        }

        On_Main.DrawNPCChatButtons += SyncGamepadDialogueFocus;
    }

    public override void Unload()
    {
        if (Main.dedServ)
        {
            return;
        }

        On_Main.DrawNPCChatButtons -= SyncGamepadDialogueFocus;
    }

    public override void PostUpdateInput()
    {
        if (Main.dedServ || !ShouldSyncFocus())
        {
            return;
        }

        EnsureDialogueStaysInGamepadUiMode();
        MirrorSignMenuDirectionsToUiDirections();
        DialogueInputGuard.ClaimUiInput(Main.LocalPlayer, "NpcDialogueGamepadFocusSystem.PostUpdateInput");

        TriggersSet triggers = PlayerInput.Triggers.Current;
        if (HasNavigationInput(triggers))
        {
            triggers.UsedMovementKey = true;
        }
    }

    private static void SyncGamepadDialogueFocus(On_Main.orig_DrawNPCChatButtons orig, int superColor, Color chatColor, int numLines, string focusText, string focusText3)
    {
        bool shouldSync = ShouldSyncFocus();
        if (shouldSync)
        {
            EnsureValidFocusPoint(focusText, focusText3);
            ApplyFocusState(focusText, focusText3);
        }

        orig(superColor, chatColor, numLines, focusText, focusText3);

        if (shouldSync)
        {
            EnsureValidFocusPoint(focusText, focusText3);
            ApplyFocusState(focusText, focusText3);
        }
    }

    private static bool ShouldSyncFocus()
    {
        bool emulatedGamepadUiActive = GamepadEmulation.GamepadEmulationState.Enabled &&
                                       PlayerInput.CurrentInputMode == InputMode.XBoxGamepadUI;
        if (!emulatedGamepadUiActive && !HasActiveGamepadUiInput())
        {
            return false;
        }

        Player player = Main.LocalPlayer;
        if (player is null || !player.active)
        {
            return false;
        }

        return player.talkNPC != -1 || player.sign != -1;
    }

    private static void EnsureDialogueStaysInGamepadUiMode()
    {
        if (PlayerInput.CurrentInputMode == InputMode.XBoxGamepadUI)
        {
            return;
        }

        if (HasActiveGamepadUiInput())
        {
            PlayerInput.CurrentInputMode = InputMode.XBoxGamepadUI;
        }
    }

    private static void MirrorSignMenuDirectionsToUiDirections()
    {
        Player player = Main.LocalPlayer;
        if (player is null || !player.active || player.sign == -1 || Main.editSign)
        {
            return;
        }

        TriggersPack pack = PlayerInput.Triggers;
        MirrorTrigger(pack, nameof(TriggersSet.MenuUp), nameof(TriggersSet.Up));
        MirrorTrigger(pack, nameof(TriggersSet.MenuDown), nameof(TriggersSet.Down));
        MirrorTrigger(pack, nameof(TriggersSet.MenuLeft), nameof(TriggersSet.Left));
        MirrorTrigger(pack, nameof(TriggersSet.MenuRight), nameof(TriggersSet.Right));
    }

    private static void MirrorTrigger(TriggersPack pack, string sourceTrigger, string targetTrigger)
    {
        if (!pack.Current.KeyStatus.TryGetValue(sourceTrigger, out bool isHeld) || !isHeld)
        {
            return;
        }

        pack.Current.KeyStatus[targetTrigger] = true;
        if (pack.Current.LatestInputMode.TryGetValue(sourceTrigger, out InputMode sourceMode))
        {
            pack.Current.LatestInputMode[targetTrigger] = sourceMode;
        }

        if (pack.JustPressed.KeyStatus.TryGetValue(sourceTrigger, out bool justPressed) && justPressed)
        {
            pack.JustPressed.KeyStatus[targetTrigger] = true;
            if (pack.JustPressed.LatestInputMode.TryGetValue(sourceTrigger, out InputMode pressedMode))
            {
                pack.JustPressed.LatestInputMode[targetTrigger] = pressedMode;
            }
        }
    }

    private static bool HasNavigationInput(TriggersSet triggers)
    {
        return triggers.Up || triggers.Down || triggers.Left || triggers.Right ||
               triggers.MenuUp || triggers.MenuDown || triggers.MenuLeft || triggers.MenuRight;
    }

    private static bool HasActiveGamepadUiInput()
    {
        try
        {
            GamePadState state = GamePad.GetState(PlayerIndex.One);
            if (!state.IsConnected)
            {
                return false;
            }

            return state.DPad.Up == ButtonState.Pressed ||
                   state.DPad.Down == ButtonState.Pressed ||
                   state.DPad.Left == ButtonState.Pressed ||
                   state.DPad.Right == ButtonState.Pressed ||
                   state.Buttons.A == ButtonState.Pressed ||
                   state.Buttons.B == ButtonState.Pressed ||
                   state.Buttons.X == ButtonState.Pressed ||
                   state.Buttons.Y == ButtonState.Pressed ||
                   state.Buttons.LeftShoulder == ButtonState.Pressed ||
                   state.Buttons.RightShoulder == ButtonState.Pressed ||
                   state.Buttons.Start == ButtonState.Pressed ||
                   state.Buttons.Back == ButtonState.Pressed ||
                   state.ThumbSticks.Left.X <= -ThumbstickThreshold ||
                   state.ThumbSticks.Left.X >= ThumbstickThreshold ||
                   state.ThumbSticks.Left.Y <= -ThumbstickThreshold ||
                   state.ThumbSticks.Left.Y >= ThumbstickThreshold;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureValidFocusPoint(string focusText, string focusText3)
    {
        List<int> activePoints = GetActivePoints(focusText, focusText3);
        if (activePoints.Count == 0)
        {
            return;
        }

        int currentPoint = UILinkPointNavigator.CurrentPoint;
        if (!activePoints.Contains(currentPoint))
        {
            UILinkPointNavigator.ChangePoint(activePoints[0]);
        }
    }

    private static void ApplyFocusState(string focusText, string focusText3)
    {
        int currentPoint = UILinkPointNavigator.CurrentPoint;
        bool hasPrimary = !string.IsNullOrWhiteSpace(focusText);
        bool hasSecondary = !string.IsNullOrWhiteSpace(focusText3);
        bool hasHappiness = HasHappinessButton();

        Main.npcChatFocus2 = hasPrimary && currentPoint == PrimaryPointId;
        Main.npcChatFocus1 = currentPoint == ClosePointId;
        Main.npcChatFocus3 = hasSecondary && currentPoint == SecondaryPointId;
        Main.npcChatFocus4 = hasHappiness && currentPoint == HappinessPointId;

        if (!Main.npcChatFocus1 && !Main.npcChatFocus2 && !Main.npcChatFocus3 && !Main.npcChatFocus4)
        {
            return;
        }

        Player player = Main.LocalPlayer;
        if (player is null)
        {
            return;
        }

        DialogueInputGuard.ClaimUiInput(player, "NpcDialogueGamepadFocusSystem.ApplyFocusState");
    }

    private static List<int> GetActivePoints(string focusText, string focusText3)
    {
        var activePoints = new List<int>(4);

        if (!string.IsNullOrWhiteSpace(focusText))
        {
            activePoints.Add(PrimaryPointId);
        }

        activePoints.Add(ClosePointId);

        if (!string.IsNullOrWhiteSpace(focusText3))
        {
            activePoints.Add(SecondaryPointId);
        }

        if (HasHappinessButton())
        {
            activePoints.Add(HappinessPointId);
        }

        return activePoints;
    }

    private static bool HasHappinessButton()
    {
        if (Main.remixWorld)
        {
            return false;
        }

        Player player = Main.LocalPlayer;
        return player is not null &&
               player.active &&
               !string.IsNullOrWhiteSpace(player.currentShoppingSettings.HappinessReport);
    }
}
