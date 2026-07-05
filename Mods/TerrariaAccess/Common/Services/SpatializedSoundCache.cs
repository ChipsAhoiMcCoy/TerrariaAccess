#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Audio;

namespace TerrariaAccess.Common.Services;

/// <summary>
/// Caches spatialized synthesized effects by normalized visible-screen X position.
/// Callers receive the dequantized normalized X value in the factory so generated buffers
/// use stable ITD positions without every emitter duplicating cache-key logic.
/// </summary>
internal sealed class SpatializedSoundCache : IDisposable
{
    private readonly Dictionary<int, SoundEffect> _effects = new();

    public SoundEffect GetOrCreate(float normalizedScreenX, Func<float, SoundEffect> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        int key = SpatializedSoundEngine.QuantizeNormalizedScreenX(normalizedScreenX);
        if (_effects.TryGetValue(key, out SoundEffect? cached) && IsEffectUsable(cached))
        {
            return cached;
        }

        if (cached is not null)
        {
            _effects.Remove(key);
            DisposeEffectQuietly(cached);
        }

        float quantizedNormalizedScreenX = SpatializedSoundEngine.DequantizeNormalizedScreenX(key);
        SoundEffect? created = factory(quantizedNormalizedScreenX);
        if (!IsEffectUsable(created))
        {
            if (created is not null)
            {
                DisposeEffectQuietly(created);
            }

            _effects.Remove(key);
            throw new InvalidOperationException("Spatialized sound factory returned an unusable SoundEffect.");
        }

        _effects[key] = created;
        return created;
    }

    public void Dispose()
    {
        foreach (SoundEffect effect in _effects.Values)
        {
            DisposeEffectQuietly(effect);
        }

        _effects.Clear();
    }

    private static void DisposeEffectQuietly(SoundEffect effect)
    {
        try
        {
            if (!effect.IsDisposed)
            {
                effect.Dispose();
            }
        }
        catch
        {
            // Ignore backend dispose failures during unload/reset.
        }
    }

    private static bool IsEffectUsable(SoundEffect? effect)
    {
        if (effect is null)
        {
            return false;
        }

        try
        {
            return !effect.IsDisposed;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Caches spatialized synthesized effects by an emitter-specific key plus normalized visible-screen X.
/// Use this when the effect also varies by frequency, waveform, loop state, or cue profile.
/// Cache keys should be quantized by callers; this cache does not evict entries because a cached
/// effect may still be backing active <see cref="SoundEffectInstance"/> objects.
/// </summary>
internal sealed class SpatializedSoundCache<TKey> : IDisposable
    where TKey : notnull
{
    private readonly Dictionary<(TKey Key, int NormalizedScreenXKey), SoundEffect> _effects = new();

    public SoundEffect GetOrCreate(TKey key, float normalizedScreenX, Func<float, SoundEffect> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        int normalizedScreenXKey = SpatializedSoundEngine.QuantizeNormalizedScreenX(normalizedScreenX);
        var cacheKey = (key, normalizedScreenXKey);
        if (_effects.TryGetValue(cacheKey, out SoundEffect? cached) && IsEffectUsable(cached))
        {
            return cached;
        }

        if (cached is not null)
        {
            _effects.Remove(cacheKey);
            DisposeEffectQuietly(cached);
        }

        float quantizedNormalizedScreenX = SpatializedSoundEngine.DequantizeNormalizedScreenX(normalizedScreenXKey);
        SoundEffect? created = factory(quantizedNormalizedScreenX);
        if (!IsEffectUsable(created))
        {
            if (created is not null)
            {
                DisposeEffectQuietly(created);
            }

            _effects.Remove(cacheKey);
            throw new InvalidOperationException("Spatialized sound factory returned an unusable SoundEffect.");
        }

        _effects[cacheKey] = created;
        return created;
    }

    public void Dispose()
    {
        foreach (SoundEffect effect in _effects.Values)
        {
            DisposeEffectQuietly(effect);
        }

        _effects.Clear();
    }

    private static void DisposeEffectQuietly(SoundEffect effect)
    {
        try
        {
            if (!effect.IsDisposed)
            {
                effect.Dispose();
            }
        }
        catch
        {
            // Ignore backend dispose failures during unload/reset.
        }
    }

    private static bool IsEffectUsable(SoundEffect? effect)
    {
        if (effect is null)
        {
            return false;
        }

        try
        {
            return !effect.IsDisposed;
        }
        catch
        {
            return false;
        }
    }
}
