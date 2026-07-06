#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.Guidance;
using Terraria;
using Terraria.ID;
using Terraria.GameInput;

namespace TerrariaAccess.Common.Systems.Audio;

/// <summary>
/// Emits pulsing audio cues for nearby hostile NPCs based on distance and threat level.
/// </summary>
internal sealed class HostileStaticEmitter : AudioEmitterBase
{
    private const int ScanIntervalTicks = 4;
    private const float StandardRangeTiles = 52f;
    private const float BossRangeTiles = 160f;
    private const int MinIntervalFrames = 7;
    private const int MaxIntervalFrames = 32;
    private const float HostileToneDurationSeconds = 0.045f;
    private const float HostileToneGain = 0.45f;
    private static readonly float[] HostileTonePartials = { 1.24f, 1.5f };
    private static readonly ToneEnvelope HostileToneEnvelope = new(attackFraction: 0.12f, releaseFraction: 0.55f, applyHannWindow: true);

    private static readonly SpatializedSoundCache s_hostileToneCache = new();

    private HostileCandidate? _primaryCandidate;
    private int _activeNpcId = -1;
    private long _nextPingFrame;
    private readonly List<SoundEffectInstance> _liveInstances = new();

    private int _ticksUntilNextScan;

    public override void Update(Player player)
    {
        if (!CanEmitAudio(player))
        {
            Reset();
            return;
        }

        // Suppress when guidance system is actively tracking hostiles
        if (GuidanceSystem.IsHostileMobTrackingActive)
        {
            Reset();
            return;
        }

        if (_ticksUntilNextScan <= 0)
        {
            _primaryCandidate = FindPrimaryCandidate(player);
            _ticksUntilNextScan = ScanIntervalTicks;
        }
        else
        {
            _ticksUntilNextScan--;
        }

        if (!_primaryCandidate.HasValue)
        {
            CleanupFinishedInstances();
            return;
        }

        HostileCandidate candidate = _primaryCandidate.Value;
        if (_activeNpcId != candidate.NpcId)
        {
            _activeNpcId = candidate.NpcId;
            _nextPingFrame = 0;
        }

        EmitIfDue(player.Center, candidate);
        CleanupFinishedInstances();
    }

    public override void Reset()
    {
        _ticksUntilNextScan = 0;
        _primaryCandidate = null;
        _activeNpcId = -1;
        _nextPingFrame = 0;
        StopAllInstances();
    }

    public void DisposeStaticResources()
    {
        StopAllInstances();
        s_hostileToneCache.Dispose();
    }

    private HostileCandidate? FindPrimaryCandidate(Player listener)
    {
        HostileCandidate? best = null;
        Vector2 listenerCenter = listener.Center;

        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (!IsEligibleHostile(npc, listener))
            {
                continue;
            }

            if (!IsLockOnEligibleForSound(npc, listenerCenter))
            {
                continue;
            }

            bool isBoss = npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[npc.type];
            float maxDistance = isBoss ? BossRangeTiles : StandardRangeTiles;
            float distanceTiles = Vector2.Distance(listenerCenter, npc.Center) / 16f;
            if (distanceTiles > maxDistance)
            {
                continue;
            }

            if (!IsWorldPositionApproximatelyOnScreen(npc.Center))
            {
                continue;
            }

            HostileCandidate candidate = new(
                npc.whoAmI,
                npc.Center,
                distanceTiles,
                maxDistance,
                isBoss);

            if (best is null || IsBetterCandidate(candidate, best.Value))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static bool IsEligibleHostile(NPC npc, Player listener)
    {
        if (!npc.active || npc.lifeMax <= 5 || npc.damage <= 0)
        {
            return false;
        }

        if (!npc.CanBeChasedBy(listener, ignoreDontTakeDamage: false))
        {
            return false;
        }

        if (npc.townNPC || npc.friendly)
        {
            return false;
        }

        return true;
    }

    private static bool IsLockOnEligibleForSound(NPC npc, Vector2 listenerCenter)
    {
        // Mirror the vanilla lock-on eligibility checks: target validity, range, on-screen window, and minimal lighting.
        if (npc is null || !npc.active || npc.dontTakeDamage || npc.friendly || npc.isLikeATownNPC || npc.life < 1 || npc.immortal)
        {
            return false;
        }

        if (npc.aiStyle == NPCAIStyleID.Mimic && npc.ai.Length > 0 && npc.ai[0] == 0f)
        {
            return false;
        }

        const float LockOnRangePixels = 2000f;
        float distance = Vector2.Distance(listenerCenter, npc.Center);
        if (distance > LockOnRangePixels)
        {
            return false;
        }

        Rectangle screenRect = Utils.CenteredRectangle(Main.player[Main.myPlayer].Center, new Vector2(1920f, 1200f));
        if (!screenRect.Intersects(npc.Hitbox))
        {
            return false;
        }

        float lightLevel = Lighting.GetSubLight(npc.Center).Length() / 3f;
        if (lightLevel < 0.03f)
        {
            return false;
        }

        return true;
    }

    private static bool IsBetterCandidate(HostileCandidate candidate, HostileCandidate current)
    {
        if (candidate.IsBoss != current.IsBoss)
        {
            return candidate.IsBoss;
        }

        return candidate.DistanceTiles < current.DistanceTiles;
    }

    private void EmitIfDue(Vector2 listenerCenter, HostileCandidate candidate)
    {
        long currentFrame = (long)Main.GameUpdateCount;
        if (currentFrame < _nextPingFrame)
        {
            return;
        }

        int delay = ComputeDelayFrames(candidate);
        PlayStaticCue(listenerCenter, candidate, delay);
        _nextPingFrame = currentFrame + Math.Max(1, delay);
    }

    private void PlayStaticCue(Vector2 listenerCenter, HostileCandidate candidate, int intervalFrames)
    {
        // Use Terraria-aligned spatial audio for hostile cues
        SpatializedSoundEngine.SpatialAudioSample sample = SpatializedSoundEngine.Compute(
            listenerCenter,
            candidate.WorldPosition,
            1f);

        float configVolume = TerrariaAccessConfig.Instance?.EnemySoundVolume ?? 1f;
        if (!sample.IsAudible(configVolume))
        {
            return;
        }

        SoundEffect tone = EnsureHostileTone(sample.NormalizedScreenX);
        if (tone.IsDisposed)
        {
            return;
        }

        SoundEffectInstance? instance = SpatializedSoundEngine.PlayWorldCue(tone, sample, configVolume);
        if (instance is not null)
        {
            _liveInstances.Add(instance);
            ReferenceToneCuePlayer.QueueGeneratedCue(
                EnsureHostileTone(SpatializedSoundEngine.CenterNormalizedScreenX),
                sample.ScaleVolume(configVolume),
                intervalFrames);
        }
    }

    private static int ComputeDelayFrames(HostileCandidate candidate)
    {
        float normalized = candidate.MaxAudibleDistanceTiles <= 0f
            ? 0f
            : Math.Clamp(candidate.DistanceTiles / candidate.MaxAudibleDistanceTiles, 0f, 1f);

        float frames = MathHelper.Lerp(MinIntervalFrames, MaxIntervalFrames, normalized);
        if (candidate.IsBoss)
        {
            frames *= 0.65f;
        }

        return GuidancePingCadence.ApplyDistanceCueRateReduction(Math.Max(1, (int)MathF.Round(frames)));
    }

    private void CleanupFinishedInstances()
    {
        SpatializedSoundEngine.CleanupStopped(_liveInstances);
    }

    private void StopAllInstances()
    {
        SpatializedSoundEngine.StopAndDisposeAll(_liveInstances);
    }

    private static SoundEffect EnsureHostileTone(float normalizedScreenX)
    {
        return s_hostileToneCache.GetOrCreate(normalizedScreenX, quantizedNormalizedScreenX =>
            SpatializedSoundEngine.CreateSpatialAdditiveTone(
            fundamentalFrequency: 360f,
            partialMultipliers: HostileTonePartials,
            envelope: HostileToneEnvelope,
            durationSeconds: HostileToneDurationSeconds,
            outputGain: HostileToneGain,
            normalizedScreenX: quantizedNormalizedScreenX,
            partialFalloff: 0.75f));
    }

    private static bool IsWorldPositionApproximatelyOnScreen(Vector2 worldPosition, float paddingPixels = 48f)
    {
        float zoomX = Math.Abs(Main.GameViewMatrix.Zoom.X) < 0.001f ? 1f : Main.GameViewMatrix.Zoom.X;
        float zoomY = Math.Abs(Main.GameViewMatrix.Zoom.Y) < 0.001f ? zoomX : Main.GameViewMatrix.Zoom.Y;
        float zoom = Math.Max(0.001f, Math.Min(zoomX, zoomY));

        float viewWidth = Main.screenWidth / zoom;
        float viewHeight = Main.screenHeight / zoom;
        Vector2 topLeft = Main.screenPosition;

        float left = topLeft.X - paddingPixels;
        float top = topLeft.Y - paddingPixels;
        float right = left + viewWidth + paddingPixels * 2f;
        float bottom = top + viewHeight + paddingPixels * 2f;

        return worldPosition.X >= left && worldPosition.X <= right &&
               worldPosition.Y >= top && worldPosition.Y <= bottom;
    }

    private readonly record struct HostileCandidate(
        int NpcId,
        Vector2 WorldPosition,
        float DistanceTiles,
        float MaxAudibleDistanceTiles,
        bool IsBoss);
}
