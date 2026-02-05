#nullable enable
using ScreenReaderMod.Common.Systems.Audio;
using Terraria;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    /// <summary>
    /// Thin wrapper that delegates to the standalone HostileStaticEmitter.
    /// This maintains backward compatibility while using the refactored Audio system.
    /// </summary>
    private sealed class HostileStaticAudioEmitter
    {
        private readonly HostileStaticEmitter _emitter = new();

        public void Update(Player listener) => _emitter.Update(listener);

        public void Reset() => _emitter.Reset();

        public static void DisposeStaticResources() => HostileStaticEmitter.DisposeStaticResources();
    }
}
