using Unity.Entities;

namespace ZeroSynth
{
    public enum UnitKind : byte
    {
        Runner = 0,
        Spitter = 1,
        Burrower = 2,
        Titan = 3,
    }

    /// <summary>
    /// Which kind an agent is. Shared so the wave director can pull dormant
    /// agents of one kind from the pool with a chunk filter, rather than
    /// scanning and testing every pooled entity.
    /// </summary>
    /// <remarks>
    /// Never changes after the pool is built, so it costs no structural
    /// changes at runtime.
    /// </remarks>
    public struct AgentKind : ISharedComponentData
    {
        public UnitKind Value;
    }

    /// <summary>What an agent's death pays out.</summary>
    public struct Bounty : IComponentData
    {
        public float Biomass;
    }

    /// <summary>Per-kind values the director applies when it releases an agent.</summary>
    public struct UnitStats
    {
        public float Health;
        public float MinSpeed;
        public float MaxSpeed;
    }

    public struct UnitTable : IComponentData
    {
        public UnitStats Runner;
        public UnitStats Spitter;
        public UnitStats Burrower;
        public UnitStats Titan;

        public UnitStats Get(UnitKind kind)
        {
            switch (kind)
            {
                case UnitKind.Spitter: return Spitter;
                case UnitKind.Burrower: return Burrower;
                case UnitKind.Titan: return Titan;
                default: return Runner;
            }
        }
    }

    /// <summary>
    /// Attacks structures from range instead of walking up to them.
    /// </summary>
    public struct RangedAttacker : IComponentData
    {
        public float Range;
        public float Damage;

        /// <summary>Shots per second.</summary>
        public float AttackRate;

        public float Cooldown;

        /// <summary>Current structure target, or Entity.Null.</summary>
        public Entity Target;

        /// <summary>Counts down to the next re-acquire. Staggered so spitters do not all rescan on one frame.</summary>
        public float RetargetTimer;
    }

    /// <summary>
    /// Standing still to attack. Steering skips agents with this enabled, so a
    /// spitter that has found a target in range stops walking.
    /// </summary>
    public struct Holding : IComponentData, IEnableableComponent { }

    /// <summary>
    /// Tunnelling. While enabled the agent ignores walls and terrain, cannot
    /// be targeted, and does not appear in the spatial grid. It surfaces near
    /// the objective, which is the whole point of the unit: it forces turret
    /// coverage at the core rather than only at the barricades.
    /// </summary>
    public struct Underground : IComponentData, IEnableableComponent { }
}
