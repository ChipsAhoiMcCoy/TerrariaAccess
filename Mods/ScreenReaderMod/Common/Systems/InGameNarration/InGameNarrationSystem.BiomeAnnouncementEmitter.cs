#nullable enable
using ScreenReaderMod.Common.Systems.Audio;
using Terraria;

namespace ScreenReaderMod.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    /// <summary>
    /// Thin wrapper that delegates to the standalone BiomeEmitter.
    /// This maintains backward compatibility while using the refactored Audio system.
    /// </summary>
    private sealed class BiomeAnnouncementEmitter
    {
        private readonly BiomeEmitter _emitter = new();

        public void Update(Player player) => _emitter.Update(player);

        public void Reset() => _emitter.Reset();
    }
}
