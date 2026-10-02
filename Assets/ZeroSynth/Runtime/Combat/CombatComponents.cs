using Unity.Entities;
using Unity.Mathematics;

namespace ZeroSynth
{
    public struct Health : IComponentData
    {
        public float Current;
        public float Max;

        public bool IsDead => Current <= 0f;
    }

    /// <summary>
    /// Enabled means the entity is live in the world; disabled means it is
    /// sitting in the pool.
    /// </summary>
    /// <remarks>
    /// Enableable rather than a plain tag so activation and death cost no
    /// structural change. Toggling this does not move the entity between
    /// archetypes, which is the whole reason the horde can churn at wave rate
    /// without stalling jobs.
    /// </remarks>
    public struct Active : IComponentData, IEnableableComponent { }

    /// <summary>
    /// A pending hit. Producers append these; only DamageApplySystem consumes
    /// them and only it writes <see cref="Health"/>.
    /// </summary>
    public struct DamageEvent
    {
        public Entity Target;
        public float Amount;
    }

    /// <summary>Where pooled entities are parked so they frustum-cull away.</summary>
    public static class PoolConstants
    {
        public static readonly float3 ParkPosition = new float3(0f, -5000f, 0f);
    }
}
