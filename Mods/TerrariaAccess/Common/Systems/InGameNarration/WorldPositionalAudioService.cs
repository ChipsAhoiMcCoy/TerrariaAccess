#nullable enable
using TerrariaAccess.Common.Services;
using TerrariaAccess.Common.Systems.Audio;
using Terraria;

namespace TerrariaAccess.Common.Systems;

public sealed partial class InGameNarrationSystem
{
    /// <summary>
    /// Thin wrapper that delegates to the standalone WorldAudioCoordinator.
    /// This maintains backward compatibility while using the refactored Audio system.
    /// </summary>
    private sealed class WorldPositionalAudioService
    {
        private readonly WorldAudioCoordinator _coordinator;

        public WorldPositionalAudioService(
            HostileStaticAudioEmitter hostileStaticAudioEmitter,
            FootstepAudioEmitter footstepAudioEmitter,
            ClimbAudioEmitter climbAudioEmitter,
            BiomeAnnouncementEmitter biomeAnnouncementEmitter,
            MultiplayerFootstepAudioEmitter multiplayerFootstepAudioEmitter)
        {
            // Create a new coordinator - the wrapper emitters are now thin delegates,
            // so the coordinator creates its own standalone emitter instances
            _coordinator = new WorldAudioCoordinator();
        }

        public void Update(NarrationServiceContext context) => _coordinator.Update(context);

        public void Reset() => _coordinator.Reset();

        public void ResetStaticResources() => _coordinator.ResetStaticResources();
    }
}
