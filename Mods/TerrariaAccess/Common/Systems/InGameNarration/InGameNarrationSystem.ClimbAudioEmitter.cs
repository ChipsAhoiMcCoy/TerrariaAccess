#nullable enable
using TerrariaAccess.Common.Systems.Audio;
using Terraria;

namespace TerrariaAccess.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    /// <summary>
    /// Thin wrapper that delegates to the standalone ClimbEmitter.
    /// This maintains backward compatibility while using the refactored Audio system.
    /// </summary>
    private sealed class ClimbAudioEmitter
    {
        private readonly ClimbEmitter _emitter = new();

        public void Update(Player player) => _emitter.Update(player);

        public void Reset() => _emitter.Reset();
    }
}
