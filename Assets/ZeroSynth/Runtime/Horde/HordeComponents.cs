using Unity.Entities;
using Unity.Mathematics;

namespace ZeroSynth
{
    /// <summary>
    /// A single member of the swarm. Kept deliberately small — this struct is
    /// touched once per agent per frame, so every byte costs cache bandwidth
    /// at horde scale.
    /// </summary>
    public struct HordeAgent : IComponentData
    {
        public float Speed;
    }

    /// <summary>
    /// Where the swarm is currently headed. A singleton for now; this is the
    /// seam the flow field will replace, at which point agents will sample a
    /// direction from the grid instead of reading one shared point.
    /// </summary>
    public struct HordeTarget : IComponentData
    {
        public float3 Position;
    }
}
