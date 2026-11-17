using Unity.Entities;
using Unity.Mathematics;

namespace ZeroSynth
{
    public enum WavePhase : byte
    {
        /// <summary>Breathing room. Build, repair, reposition.</summary>
        Preparing = 0,

        /// <summary>Agents are being released from the pool.</summary>
        Spawning = 1,

        /// <summary>Everything is out; waiting for the field to clear.</summary>
        Fighting = 2,
    }

    /// <summary>
    /// Global run state. <see cref="Paused"/> is read by the tactical pause in
    /// M7, which gates the gameplay group rather than scaling time.
    /// </summary>
    public struct SimClock : IComponentData
    {
        public bool Paused;
        public float Speed;
        public float Elapsed;
    }

    public struct Economy : IComponentData
    {
        /// <summary>From destroyed hives and swarm kills. Feeds foundries.</summary>
        public float Biomass;

        /// <summary>From ruined cities and cleared waves. Builds structures.</summary>
        public float MetalScrap;
    }

    /// <summary>
    /// Everything the wave director needs to put an agent into the world.
    /// Held as a singleton so spawning lives in a system rather than in the
    /// MonoBehaviour that happens to own the pool.
    /// </summary>
    public struct SpawnProfile : IComponentData
    {
        public float InnerRadius;
        public float OuterRadius;

        /// <summary>Y a tunnelling agent travels at. Below the ground plane, so it is hidden.</summary>
        public float BurrowDepth;

        /// <summary>Distance from the objective at which a tunnelling agent surfaces.</summary>
        public float BurrowSurfaceRadius;

        /// <summary>Tunnelling is faster than walking; the dirt does not path around walls.</summary>
        public float BurrowSpeedMultiplier;
    }

    public struct WaveConfig : IComponentData
    {
        /// <summary>Run seed. Wave composition is derived from this, so a run is reproducible.</summary>
        public uint Seed;

        /// <summary>Runner count in wave 1. Other kinds scale from their own base.</summary>
        public int BaseCount;

        public int SpitterBase;
        public int SpitterFirstWave;

        public int BurrowerBase;
        public int BurrowerFirstWave;

        /// <summary>A Titan arrives every Nth wave. Zero disables bosses.</summary>
        public int TitanEvery;

        /// <summary>Fractional growth per wave. 0.25 means +25% each time.</summary>
        public float CountGrowth;

        /// <summary>Seconds of prep before a wave starts.</summary>
        public float PrepareDuration;

        /// <summary>Agents released per second during the spawn phase.</summary>
        public float SpawnRate;

        /// <summary>Per-wave speed multiplier growth.</summary>
        public float SpeedGrowth;

        /// <summary>Scrap awarded for clearing a wave.</summary>
        public float ScrapPerWave;
    }

    public struct WaveState : IComponentData
    {
        /// <summary>1-based. Zero means the run has not started.</summary>
        public int Index;

        public WavePhase Phase;

        /// <summary>Counts down in Preparing, up elsewhere.</summary>
        public float Timer;

        /// <summary>
        /// Still to be released this wave, per kind: x Runner, y Spitter,
        /// z Burrower, w Titan.
        /// </summary>
        public int4 Remaining;

        public int RemainingTotal => Remaining.x + Remaining.y + Remaining.z + Remaining.w;

        /// <summary>Fractional spawn carry, so low rates still emit smoothly.</summary>
        public float SpawnAccumulator;

        /// <summary>Total released this wave, for the readout.</summary>
        public int Released;
    }

    /// <summary>
    /// Build costs. Read by the build UI in M7; kept here so the economy and
    /// the prices live in one place rather than drifting apart in the UI.
    /// </summary>
    public static class StructureCosts
    {
        public static float Scrap(StructureKind kind)
        {
            switch (kind)
            {
                case StructureKind.Wall: return 20f;
                case StructureKind.Turret: return 120f;
                case StructureKind.Pylon: return 60f;
                case StructureKind.Reactor: return 300f;
                case StructureKind.Foundry: return 250f;
                default: return 0f;
            }
        }
    }
}
