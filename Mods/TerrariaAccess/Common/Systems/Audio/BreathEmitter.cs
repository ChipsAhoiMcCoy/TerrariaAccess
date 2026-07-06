#nullable enable
using System;
using TerrariaAccess.Common.Services;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace TerrariaAccess.Common.Systems.Audio;

/// <summary>
/// Monitors the player's breath while submerged and emits audio/verbal cues
/// at every 10% of breath lost. Announces when breath begins depleting and
/// plays Terraria's native drowning cue at each threshold.
/// </summary>
internal sealed class BreathEmitter : AudioEmitterBase
{
    private const int ThresholdStep = 10;
    private const float DrowningCueVolume = 0.65f;

    /// <summary>
    /// Tracks whether the player was previously at full breath (not submerged).
    /// Used to detect the transition into breath depletion.
    /// </summary>
    private bool _wasAtFullBreath = true;

    /// <summary>
    /// The last 10% threshold that was announced (100 = full, 90, 80, ..., 0).
    /// </summary>
    private int _lastAnnouncedThreshold = 100;

    public override void Update(Player player)
    {
        if (!CanEmitAudio(player))
        {
            ResetState();
            return;
        }

        int breath = player.breath;
        int breathMax = player.breathMax;

        if (breathMax <= 0)
        {
            return;
        }

        // Calculate current breath percentage (0-100)
        int breathPercent = (int)Math.Round(100.0 * breath / breathMax);
        breathPercent = Math.Clamp(breathPercent, 0, 100);

        // Determine the current 10% threshold bucket
        // e.g., 95% -> threshold 90, 83% -> threshold 80, 10% -> threshold 10, 5% -> threshold 0
        int currentThreshold = (breathPercent / ThresholdStep) * ThresholdStep;

        // Detect transition from full breath to depleting
        if (_wasAtFullBreath && breath < breathMax)
        {
            _wasAtFullBreath = false;
            _lastAnnouncedThreshold = 100;
            ScreenReaderService.Announce("Breath depleting", force: true);
            return;
        }

        // Detect recovery back to full breath
        if (!_wasAtFullBreath && breath >= breathMax)
        {
            _wasAtFullBreath = true;
            _lastAnnouncedThreshold = 100;
            ScreenReaderService.Announce("Breath restored", force: true);
            return;
        }

        // If at full breath, nothing to do
        if (_wasAtFullBreath)
        {
            return;
        }

        // Check if we've crossed into a new 10% threshold (going down)
        if (currentThreshold < _lastAnnouncedThreshold)
        {
            _lastAnnouncedThreshold = currentThreshold;
            PlayBreathWarningSound();
            AnnounceBreathThreshold(currentThreshold);
        }
    }

    public override void Reset()
    {
        ResetState();
    }

    private void ResetState()
    {
        _wasAtFullBreath = true;
        _lastAnnouncedThreshold = 100;
    }

    private static void PlayBreathWarningSound()
    {
        if (Main.dedServ)
        {
            return;
        }

        float configVolume = TerrariaAccessConfig.Instance?.GuidanceVolume ?? 1f;
        float volume = Math.Clamp(DrowningCueVolume * configVolume, 0f, 1f);
        if (volume <= 0f)
        {
            return;
        }

        SoundEngine.PlaySound(SoundID.Drown.WithVolumeScale(volume));
    }

    private static void AnnounceBreathThreshold(int thresholdPercent)
    {
        string message = thresholdPercent switch
        {
            0 => "Drowning!",
            _ => $"Breath {thresholdPercent} percent",
        };

        ScreenReaderService.Announce(message, force: true);
    }
}
