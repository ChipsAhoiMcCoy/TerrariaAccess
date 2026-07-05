#nullable enable
using System;
using Terraria;

namespace TerrariaAccess.Common.Services;

/// <summary>
/// Suppresses synchronous native Terraria sounds for accessibility-driven actions that provide
/// explicit Terraria Access custom audio feedback instead.
/// </summary>
internal static class NativeSoundSuppression
{
    private static uint s_itemSlotSuppressionFrame;
    private static bool s_itemSlotSuppressionActive;
    private static int s_synchronousSuppressionDepth;
    private static float s_synchronousPreviousSoundVolume;
    private static uint s_deferredSuppressionFrame;
    private static float s_deferredPreviousSoundVolume;
    private static bool s_deferredSuppressionActive;

    public static void RunSynchronous(Action action)
    {
        float previousSoundVolume = BeginSynchronousSuppression();

        try
        {
            action();
        }
        finally
        {
            EndSynchronousSuppression(previousSoundVolume);
        }
    }

    public static float BeginSynchronousSuppression()
    {
        RestoreExpiredDeferredSuppression();

        float previousSoundVolume = Main.soundVolume;
        if (s_synchronousSuppressionDepth == 0)
        {
            s_synchronousPreviousSoundVolume = GetEffectiveSoundVolume();
            Main.soundVolume = 0f;
        }

        s_synchronousSuppressionDepth++;
        return previousSoundVolume;
    }

    public static void EndSynchronousSuppression(float previousSoundVolume)
    {
        RestoreExpiredDeferredSuppression();

        if (s_synchronousSuppressionDepth <= 0)
        {
            Main.soundVolume = previousSoundVolume;
            return;
        }

        s_synchronousSuppressionDepth--;
        if (s_synchronousSuppressionDepth == 0)
        {
            Main.soundVolume = s_deferredSuppressionActive ? 0f : s_synchronousPreviousSoundVolume;
            s_synchronousPreviousSoundVolume = 0f;
        }
    }

    public static void RequestItemSlotClickSuppression()
    {
        s_itemSlotSuppressionFrame = Main.GameUpdateCount;
        s_itemSlotSuppressionActive = true;
    }

    public static bool ShouldSuppressItemSlotClick()
    {
        if (!s_itemSlotSuppressionActive)
        {
            return false;
        }

        if (s_itemSlotSuppressionFrame == Main.GameUpdateCount)
        {
            return true;
        }

        s_itemSlotSuppressionActive = false;
        return false;
    }

    public static void RunItemSlotClick(Action action)
    {
        if (!ShouldSuppressItemSlotClick())
        {
            action();
            return;
        }

        RunSynchronous(action);
    }

    public static void RequestDeferredSuppressionForCurrentFrame()
    {
        RestoreExpiredDeferredSuppression();

        uint currentFrame = Main.GameUpdateCount;
        if (s_deferredSuppressionActive && s_deferredSuppressionFrame == currentFrame)
        {
            return;
        }

        RestoreDeferredSuppression();
        s_deferredPreviousSoundVolume = GetEffectiveSoundVolume();
        s_deferredSuppressionFrame = currentFrame;
        s_deferredSuppressionActive = true;
        Main.soundVolume = 0f;
    }

    public static void RestoreDeferredSuppression()
    {
        if (!s_deferredSuppressionActive)
        {
            return;
        }

        if (s_synchronousSuppressionDepth == 0 &&
            Main.soundVolume == 0f &&
            s_deferredPreviousSoundVolume > 0f)
        {
            Main.soundVolume = s_deferredPreviousSoundVolume;
        }

        s_deferredSuppressionActive = false;
    }

    public static float GetEffectiveSoundVolume()
    {
        RestoreExpiredDeferredSuppression();

        if (s_synchronousSuppressionDepth > 0)
        {
            return s_synchronousPreviousSoundVolume;
        }

        if (s_deferredSuppressionActive)
        {
            return s_deferredPreviousSoundVolume;
        }

        return Main.soundVolume;
    }

    public static void ResetState()
    {
        if (s_synchronousSuppressionDepth > 0)
        {
            Main.soundVolume = s_synchronousPreviousSoundVolume;
        }
        else
        {
            RestoreDeferredSuppression();
        }

        s_itemSlotSuppressionFrame = 0;
        s_itemSlotSuppressionActive = false;
        s_synchronousSuppressionDepth = 0;
        s_synchronousPreviousSoundVolume = 0f;
        s_deferredSuppressionFrame = 0;
        s_deferredPreviousSoundVolume = 0f;
        s_deferredSuppressionActive = false;
    }

    private static void RestoreExpiredDeferredSuppression()
    {
        if (s_deferredSuppressionActive && s_deferredSuppressionFrame != Main.GameUpdateCount)
        {
            RestoreDeferredSuppression();
        }
    }
}
