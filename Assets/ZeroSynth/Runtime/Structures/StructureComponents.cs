using Unity.Entities;
using Unity.Mathematics;

namespace ZeroSynth
{
    public enum StructureKind : byte
    {
        Wall = 0,
        Turret = 1,
        Reactor = 2,
        Foundry = 3,
        Pylon = 4,

        /// <summary>The objective. Losing it ends the run.</summary>
        Core = 5,
    }

    /// <summary>
    /// Marks the objective structure. Exactly one exists per run; the game
    /// flow reads its health for the lose condition.
    /// </summary>
    public struct Core : IComponentData { }

    public struct Structure : IComponentData
    {
        public StructureKind Kind;

        /// <summary>Size in cells. (1,1) for most things.</summary>
        public int2 Footprint;
    }

    /// <summary>
    /// A structure's authoritative position. The world transform is derived
    /// from this, not the other way round, so placement and pathfinding can
    /// never disagree about which cells a structure occupies.
    /// </summary>
    public struct GridAnchor : IComponentData
    {
        public int2 Cell;
    }

    /// <summary>Barricade. Traversable at a cost, and attackable.</summary>
    public struct Wall : IComponentData { }

    /// <summary>
    /// Turns biomass from kills into scrap while powered. This is what gives
    /// biomass a use: it is otherwise only ever collected.
    /// </summary>
    public struct Foundry : IComponentData
    {
        /// <summary>Biomass consumed per second at full output.</summary>
        public float BiomassPerSecond;

        /// <summary>Scrap produced for each biomass consumed.</summary>
        public float ScrapPerBiomass;
    }

    /// <summary>
    /// Melee capability. Separate from <see cref="HordeAgent"/> so the
    /// movement job does not drag combat fields through cache every frame.
    /// </summary>
    public struct AgentCombat : IComponentData
    {
        public float Damage;

        /// <summary>Attacks per second.</summary>
        public float AttackRate;

        public float Cooldown;

        /// <summary>How far ahead a wall is attacked from, in world units.</summary>
        public float Reach;
    }

    public static class WallTuning
    {
        /// <summary>
        /// Traversal cost of a wall at full health, on top of the base cost of
        /// 1 for open ground. High enough that the field always prefers an open
        /// route when one exists, so walls genuinely redirect rather than merely
        /// slow the swarm.
        /// </summary>
        public const float FullHealthCost = 60f;

        /// <summary>
        /// Cost floor for a nearly-destroyed wall. Never zero, or a breached
        /// wall would look more attractive than the open ground beside it.
        /// </summary>
        public const float MinCost = 2f;
    }
}
