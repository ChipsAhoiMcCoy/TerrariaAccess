#nullable enable
using System;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.RuntimeDetour;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace ScreenReaderMod.Common.Systems;

/// <summary>
/// Fixes UILinkPoint navigation in the achievements menu.
/// Terraria's SetupGamepadPoints sets category button Down links to the back button (3000)
/// instead of the achievements list (3001), causing navigation to skip the list when pressing down.
/// Also plays the menu tick sound when navigating to the achievements list since it lacks
/// the OnMouseOver handler that category buttons have.
/// </summary>
public sealed class AchievementsMenuGamepadSystem : ModSystem
{
    private const int BackButtonLinkId = 3000;
    private const int AchievementsListLinkId = 3001;
    private const int FirstCategoryButtonLinkId = 3002;
    private const int MaxCategoryButtons = 5;

    private static readonly Type? AchievementsMenuType = Type.GetType("Terraria.GameContent.UI.States.UIAchievementsMenu, tModLoader")
        ?? Type.GetType("Terraria.GameContent.UI.States.UIAchievementsMenu, Terraria");

    private delegate void DrawDelegate(UIState self, SpriteBatch spriteBatch);
    private static Hook? _drawHook;

    private static int _lastCurrentPoint = -1;

    public override void Load()
    {
        if (AchievementsMenuType is null)
        {
            return;
        }

        MethodInfo? drawMethod = AchievementsMenuType.GetMethod("Draw", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(SpriteBatch) }, null);
        if (drawMethod is null)
        {
            ScreenReaderMod.Instance?.Logger.Warn("[AchievementsMenuGamepad] Could not find UIAchievementsMenu.Draw method");
            return;
        }

        _drawHook = new Hook(drawMethod, OnDraw);
    }

    public override void Unload()
    {
        _drawHook?.Dispose();
        _drawHook = null;
        _lastCurrentPoint = -1;
    }

    private static void OnDraw(DrawDelegate orig, UIState self, SpriteBatch spriteBatch)
    {
        // Call original Draw which includes SetupGamepadPoints
        orig(self, spriteBatch);

        // Now fix the navigation after SetupGamepadPoints has run
        FixCategoryButtonNavigation();

        // Play tick sound when navigating to achievements list from category buttons
        PlayNavigationSoundIfNeeded();
    }

    private static void FixCategoryButtonNavigation()
    {
        // Terraria's SetupGamepadPoints sets category button Down to 3000 (back button).
        // We fix this to point to 3001 (achievements list) so pressing down from filters goes to achievements.
        for (int i = 0; i < MaxCategoryButtons; i++)
        {
            int linkId = FirstCategoryButtonLinkId + i;
            if (!UILinkPointNavigator.Points.TryGetValue(linkId, out UILinkPoint? linkPoint) || linkPoint is null)
            {
                continue;
            }

            // Only fix if Down is currently pointing to back button
            if (linkPoint.Down == BackButtonLinkId)
            {
                linkPoint.Down = AchievementsListLinkId;
            }
        }
    }

    private static void PlayNavigationSoundIfNeeded()
    {
        if (!UILinkPointNavigator.InUse)
        {
            _lastCurrentPoint = -1;
            return;
        }

        int currentPoint = UILinkPointNavigator.CurrentPoint;

        // Detect navigation from category buttons to achievements list
        // Category buttons have OnMouseOver handlers that play sounds, but the achievements list doesn't
        if (currentPoint == AchievementsListLinkId &&
            _lastCurrentPoint >= FirstCategoryButtonLinkId &&
            _lastCurrentPoint < FirstCategoryButtonLinkId + MaxCategoryButtons)
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
        }

        _lastCurrentPoint = currentPoint;
    }
}
