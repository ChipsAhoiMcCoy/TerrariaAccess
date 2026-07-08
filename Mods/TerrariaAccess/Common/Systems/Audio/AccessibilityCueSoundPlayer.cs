#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Terraria;
using Terraria.ModLoader;
using TerrariaAccess.Common.Services;

namespace TerrariaAccess.Common.Systems.Audio;

internal static class AccessibilityCueSoundPlayer
{
    private static readonly List<SoundEffectInstance> ActiveInstances = new();
    private static readonly Dictionary<string, PcmAssetData> PcmAssetCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<SpatialAssetCacheKey, SoundEffect> InterauralAssetCache = new();

    public static SoundEffectInstance? PlayCentered(
        string assetPath,
        float volume,
        float pitch = 0f,
        bool looped = false)
    {
        return Play(assetPath, volume, pan: 0f, pitch, looped);
    }

    public static SoundEffectInstance? PlaySpatial(
        string assetPath,
        SpatializedSoundEngine.SpatialAudioSample sample,
        float localVolumeScale,
        bool looped = false)
    {
        return Play(
            assetPath,
            sample.ScaleVolume(localVolumeScale),
            sample.NormalizedScreenX,
            sample.Pitch,
            looped);
    }

    public static SoundEffectInstance? PlaySpatialInteraural(
        string assetPath,
        SpatializedSoundEngine.SpatialAudioSample sample,
        float localVolumeScale,
        bool looped = false)
    {
        float volume = sample.ScaleVolume(localVolumeScale);
        if (string.IsNullOrWhiteSpace(assetPath) || !SpatializedSoundEngine.CanPlay(volume))
        {
            return null;
        }

        try
        {
            CleanupFinishedInstances();
            SoundEffect effect = EnsureInterauralAsset(assetPath, sample.NormalizedScreenX);
            SoundEffectInstance? instance = SpatializedSoundEngine.PlayAlreadySpatializedWorldCue(
                effect,
                volume,
                sample.Pitch,
                looped);

            if (instance is not null)
            {
                ActiveInstances.Add(instance);
            }

            return instance;
        }
        catch (Exception ex)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Warn(
                $"[AccessibilityCueSoundPlayer] Failed to play interaural asset cue '{assetPath}': {ex.Message}");

            return PlaySpatial(assetPath, sample, localVolumeScale, looped);
        }
    }

    public static void SetSpatial(
        SoundEffectInstance? instance,
        SpatializedSoundEngine.SpatialAudioSample sample,
        float localVolumeScale)
    {
        SetInstanceState(
            instance,
            sample.ScaleVolume(localVolumeScale),
            sample.NormalizedScreenX,
            sample.Pitch);
    }

    public static void SetVolume(SoundEffectInstance? instance, float volume)
    {
        if (!IsInstanceUsable(instance))
        {
            return;
        }

        try
        {
            instance!.Volume = ComputeOutputVolume(volume);
        }
        catch
        {
            // Ignore backend failures for stale instances.
        }
    }

    public static void StopInstance(SoundEffectInstance? instance)
    {
        if (instance is null)
        {
            return;
        }

        SpatializedSoundEngine.StopAndDispose(instance);
        ActiveInstances.Remove(instance);
    }

    public static void DisposeStaticResources()
    {
        SpatializedSoundEngine.StopAndDisposeAll(ActiveInstances);
        DisposeInterauralAssets();
    }

    private static SoundEffectInstance? Play(
        string assetPath,
        float volume,
        float pan,
        float pitch,
        bool looped)
    {
        if (string.IsNullOrWhiteSpace(assetPath) || !SpatializedSoundEngine.CanPlay(volume))
        {
            return null;
        }

        try
        {
            CleanupFinishedInstances();

            SoundEffect effect = ModContent.Request<SoundEffect>(assetPath).Value;
            if (effect is null || effect.IsDisposed)
            {
                return null;
            }

            SoundEffectInstance? instance = null;
            try
            {
                instance = effect.CreateInstance();
                instance.IsLooped = looped;
                SetInstanceState(instance, volume, pan, pitch);
                instance.Play();
                ActiveInstances.Add(instance);
                return instance;
            }
            catch
            {
                SpatializedSoundEngine.StopAndDispose(instance);
                return null;
            }
        }
        catch (Exception ex)
        {
            global::TerrariaAccess.TerrariaAccess.Instance?.Logger.Warn(
                $"[AccessibilityCueSoundPlayer] Failed to play asset cue '{assetPath}': {ex.Message}");
            return null;
        }
    }

    private static void SetInstanceState(SoundEffectInstance? instance, float volume, float pan, float pitch)
    {
        if (!IsInstanceUsable(instance))
        {
            return;
        }

        try
        {
            instance!.Volume = ComputeOutputVolume(volume);
            instance.Pan = ClampPan(pan);
            instance.Pitch = ClampPitch(pitch);
        }
        catch
        {
            // Ignore backend failures for stale instances.
        }
    }

    private static void CleanupFinishedInstances()
    {
        SpatializedSoundEngine.CleanupStopped(ActiveInstances);
    }

    private static SoundEffect EnsureInterauralAsset(string assetPath, float normalizedScreenX)
    {
        int normalizedScreenXKey = SpatializedSoundEngine.QuantizeNormalizedScreenX(normalizedScreenX);
        SpatialAssetCacheKey cacheKey = new(assetPath, normalizedScreenXKey);
        if (InterauralAssetCache.TryGetValue(cacheKey, out SoundEffect? cached) &&
            cached is not null &&
            !cached.IsDisposed)
        {
            return cached;
        }

        PcmAssetData pcm = EnsurePcmAsset(assetPath);
        float quantizedScreenX = SpatializedSoundEngine.DequantizeNormalizedScreenX(normalizedScreenXKey);
        SoundEffect effect = SpatializedSoundEngine.CreateSpatialFromSamples(
            pcm.Samples,
            pcm.SampleRate,
            quantizedScreenX);
        InterauralAssetCache[cacheKey] = effect;
        return effect;
    }

    private static PcmAssetData EnsurePcmAsset(string assetPath)
    {
        if (PcmAssetCache.TryGetValue(assetPath, out PcmAssetData cached))
        {
            return cached;
        }

        string filePath = assetPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
            ? assetPath
            : assetPath + ".wav";
        byte[] bytes = ModContent.GetFileBytes(filePath);
        PcmAssetData data = DecodePcm16Wav(bytes, filePath);
        PcmAssetCache[assetPath] = data;
        return data;
    }

    private static PcmAssetData DecodePcm16Wav(byte[] bytes, string filePath)
    {
        if (bytes.Length < 44 ||
            ReadAscii(bytes, 0, 4) != "RIFF" ||
            ReadAscii(bytes, 8, 4) != "WAVE")
        {
            throw new InvalidOperationException($"'{filePath}' is not a RIFF/WAVE file.");
        }

        int channels = 0;
        int sampleRate = 0;
        int bitsPerSample = 0;
        int dataOffset = -1;
        int dataSize = 0;

        for (int offset = 12; offset + 8 <= bytes.Length;)
        {
            string chunkId = ReadAscii(bytes, offset, 4);
            int chunkSize = BitConverter.ToInt32(bytes, offset + 4);
            int chunkDataOffset = offset + 8;
            if (chunkSize < 0 || chunkDataOffset + chunkSize > bytes.Length)
            {
                throw new InvalidOperationException($"'{filePath}' contains an invalid WAV chunk.");
            }

            if (chunkId == "fmt ")
            {
                if (chunkSize < 16)
                {
                    throw new InvalidOperationException($"'{filePath}' contains an invalid fmt chunk.");
                }

                short formatTag = BitConverter.ToInt16(bytes, chunkDataOffset);
                channels = BitConverter.ToInt16(bytes, chunkDataOffset + 2);
                sampleRate = BitConverter.ToInt32(bytes, chunkDataOffset + 4);
                bitsPerSample = BitConverter.ToInt16(bytes, chunkDataOffset + 14);
                if (formatTag != 1)
                {
                    throw new InvalidOperationException($"'{filePath}' is not PCM WAV data.");
                }
            }
            else if (chunkId == "data")
            {
                dataOffset = chunkDataOffset;
                dataSize = chunkSize;
            }

            offset = chunkDataOffset + chunkSize + (chunkSize & 1);
        }

        if (dataOffset < 0 || sampleRate <= 0 || bitsPerSample != 16 || channels <= 0)
        {
            throw new InvalidOperationException($"'{filePath}' must be 16-bit PCM WAV data.");
        }

        int frameSize = channels * sizeof(short);
        int frameCount = dataSize / frameSize;
        float[] samples = new float[Math.Max(1, frameCount)];
        for (int frame = 0; frame < frameCount; frame++)
        {
            float mixed = 0f;
            int frameOffset = dataOffset + frame * frameSize;
            for (int channel = 0; channel < channels; channel++)
            {
                short sample = BitConverter.ToInt16(bytes, frameOffset + channel * sizeof(short));
                mixed += sample / 32768f;
            }

            samples[frame] = MathHelper.Clamp(mixed / channels, -1f, 1f);
        }

        return new PcmAssetData(samples, sampleRate);
    }

    private static string ReadAscii(byte[] bytes, int offset, int count)
    {
        return System.Text.Encoding.ASCII.GetString(bytes, offset, count);
    }

    private static void DisposeInterauralAssets()
    {
        foreach (SoundEffect effect in InterauralAssetCache.Values)
        {
            try
            {
                effect.Dispose();
            }
            catch
            {
                // Ignore backend dispose failures during unload/reset.
            }
        }

        InterauralAssetCache.Clear();
        PcmAssetCache.Clear();
    }

    private static float ComputeOutputVolume(float volume)
    {
        return ClampVolume(volume) *
            ClampVolume(NativeSoundSuppression.GetEffectiveSoundVolume()) *
            AudioVolumeDefaults.WorldCueVolumeScale;
    }

    private static bool IsInstanceUsable(SoundEffectInstance? instance)
    {
        if (instance is null)
        {
            return false;
        }

        try
        {
            return !instance.IsDisposed;
        }
        catch
        {
            return false;
        }
    }

    private static float ClampVolume(float volume) =>
        float.IsFinite(volume) ? MathHelper.Clamp(volume, 0f, 1f) : 0f;

    private static float ClampPan(float pan) =>
        float.IsFinite(pan) ? MathHelper.Clamp(pan, -1f, 1f) : 0f;

    private static float ClampPitch(float pitch) =>
        float.IsFinite(pitch) ? MathHelper.Clamp(pitch, -1f, 1f) : 0f;

    private readonly record struct SpatialAssetCacheKey(string AssetPath, int NormalizedScreenXKey);

    private readonly record struct PcmAssetData(float[] Samples, int SampleRate);
}
