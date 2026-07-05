#nullable enable
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.Audio;
using Terraria;

namespace TerrariaAccess.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    /// <summary>
    /// Owns the standalone world-audio coordinator used by in-game narration scheduling.
    /// </summary>
    private sealed class WorldPositionalAudioService
    {
        private readonly WorldAudioCoordinator _coordinator;

        public WorldPositionalAudioService()
        {
            _coordinator = new WorldAudioCoordinator();
        }

        public void Update(NarrationServiceContext context) => _coordinator.Update(context);

        public void Reset() => _coordinator.Reset();

        public void ResetStaticResources() => _coordinator.ResetStaticResources();
    }
}
