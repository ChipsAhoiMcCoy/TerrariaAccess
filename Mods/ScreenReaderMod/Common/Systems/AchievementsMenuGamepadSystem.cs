#nullable enable
using System;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Systems;

/// <summary>
/// Fixes UILinkPoint navigation in the achievements menu to start on the achievements list
/// instead of the category filter buttons.
/// </summary>
public sealed class AchievementsMenuGamepadSystem : ModSystem
{
    private const int AchievementsListLinkId = 3001;
    private const int FirstCategoryButtonLinkId = 3002;
    private const int MaxCategoryButtons = 5;

    private static readonly Type? AchievementsMenuType = Type.GetType("Terraria.GameContent.UI.States.UIAchievementsMenu, tModLoader")
        ?? Type.GetType("Terraria.GameContent.UI.States.UIAchievementsMenu, Terraria");

    private delegate void DrawDelegate(UIState self, SpriteBatch spriteBatch);
    private delegate void OnActivateDelegate(UIState self);

    private static Hook? _drawHook;
    private static Hook? _onActivateHook;
    private static Hook? _changePointHook;

    /// <summary>
    /// Number of frames to redirect ChangePoint calls from category buttons to achievements list.
    /// </summary>
    private static int _redirectFramesRemaining;

    private static int _lastCurrentPoint = -1;
    private static bool _playedInitialSound;

    /// <summary>
    /// Returns true if the achievements menu is currently active and handling gamepad input.
    /// Used by MenuNarration to suppress hover/focus announcements that would conflict.
    /// </summary>
    public static bool IsHandlingGamepadInput
    {
        get
        {
            if (!PlayerInput.UsingGamepadUI)
            {
                return false;
            }

            UIState? currentState = Main.MenuUI?.CurrentState;
            if (currentState is null || AchievementsMenuType is null)
            {
                return false;
            }

            return currentState.GetType() == AchievementsMenuType;
        }
    }

    public override void Load()
    {
        if (AchievementsMenuType is null)
        {
            return;
        }

        MethodInfo? drawMethod = AchievementsMenuType.GetMethod("Draw", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(SpriteBatch) }, null);
        if (drawMethod is not null)
        {
            _drawHook = new Hook(drawMethod, OnDraw);
        }

        MethodInfo? onActivateMethod = AchievementsMenuType.GetMethod("OnActivate", BindingFlags.Instance | BindingFlags.Public);
        if (onActivateMethod is not null)
        {
            _onActivateHook = new Hook(onActivateMethod, OnActivate);
        }

        // Hook ChangePoint to intercept focus changes during activation
        MethodInfo? changePointMethod = typeof(UILinkPointNavigator).GetMethod(
            "ChangePoint",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new[] { typeof(int) },
            null);
        if (changePointMethod is not null)
        {
            _changePointHook = new Hook(changePointMethod, OnChangePoint);
        }
    }

    public override void Unload()
    {
        _drawHook?.Dispose();
        _drawHook = null;
        _onActivateHook?.Dispose();
        _onActivateHook = null;
        _changePointHook?.Dispose();
        _changePointHook = null;
        _redirectFramesRemaining = 0;
        _lastCurrentPoint = -1;
        _playedInitialSound = false;
    }

    private static void OnChangePoint(Action<int> orig, int id)
    {
        // During activation window, redirect category button focus to achievements list
        if (_redirectFramesRemaining > 0 && id == FirstCategoryButtonLinkId)
        {
            id = AchievementsListLinkId;
        }
        orig(id);
    }

    private static void OnActivate(OnActivateDelegate orig, UIState self)
    {
        // Redirect ChangePoint(3002) calls to 3001 for several frames after activation
        _redirectFramesRemaining = 10;
        _lastCurrentPoint = -1;
        _playedInitialSound = false;
        orig(self);

        // Prevent residual directional input from navigating away from achievements list
        // The achievements list (3001) has Up=3002, so any Up input would move to category buttons
        UILinkPointNavigator.ForceMovementCooldown(15);
    }

    private static void OnDraw(DrawDelegate orig, UIState self, SpriteBatch spriteBatch)
    {
        orig(self, spriteBatch);

        // Decrement redirect counter each frame
        if (_redirectFramesRemaining > 0)
        {
            _redirectFramesRemaining--;
        }

        PlayNavigationSoundIfNeeded();
    }

    private static void PlayNavigationSoundIfNeeded()
    {
        if (!UILinkPointNavigator.InUse)
        {
            _lastCurrentPoint = -1;
            return;
        }

        int currentPoint = UILinkPointNavigator.CurrentPoint;

        // Play tick sound when navigating to achievements list from category buttons
        // Only play once per transition to prevent spamming
        if (currentPoint == AchievementsListLinkId &&
            _lastCurrentPoint >= FirstCategoryButtonLinkId &&
            _lastCurrentPoint < FirstCategoryButtonLinkId + MaxCategoryButtons &&
            !_playedInitialSound)
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
            _playedInitialSound = true;
        }

        // Reset sound flag when navigating away from achievements list
        if (currentPoint != AchievementsListLinkId)
        {
            _playedInitialSound = false;
        }

        _lastCurrentPoint = currentPoint;
    }
}
