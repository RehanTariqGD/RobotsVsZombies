using Unity.Entities;

namespace ZeroSynth.Flow
{
    /// <summary>
    /// Throws away the ECS world and builds a fresh one.
    /// </summary>
    /// <remarks>
    /// The default world survives scene loads. Every gameplay MonoBehaviour
    /// creates its entities in Start (grid, pools, run state, structures), so
    /// loading the game scene a second time would stack a second copy of all
    /// of it on top of the first. Resetting the world between runs is the
    /// simple, complete fix; it also resets every system's internal state.
    ///
    /// Must run while no gameplay scene is loaded, so the scene's OnDestroy
    /// callbacks have already released their native memory.
    /// </remarks>
    public static class EcsWorldReset
    {
        public static void Reset()
        {
            World.DisposeAllWorlds();
            DefaultWorldInitialization.Initialize("Default World", false);
        }
    }
}
