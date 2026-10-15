using Unity.Entities;

namespace ZeroSynth
{
    public enum TargetingStance : byte
    {
        /// <summary>Nearest agent. Best against leakers.</summary>
        Closest = 0,

        /// <summary>Most remaining health. Best against titans.</summary>
        Strongest = 1,

        /// <summary>Highest speed. Best against runners reaching the wall first.</summary>
        Fastest = 2,
    }

    public struct Turret : IComponentData
    {
        public float Range;

        /// <summary>Shots per second.</summary>
        public float FireRate;

        public float Damage;

        /// <summary>Zero means single target.</summary>
        public float SplashRadius;

        /// <summary>Counts down; a shot fires at or below zero.</summary>
        public float Cooldown;
    }

    public struct TurretStance : IComponentData
    {
        public TargetingStance Value;
    }

    /// <summary>
    /// Current target. <see cref="Entity.Null"/> when nothing is in range.
    /// </summary>
    public struct TurretTarget : IComponentData
    {
        public Entity Value;
    }

    /// <summary>
    /// Player is driving this turret. Suppresses auto-targeting and applies
    /// the manual-aim bonus from the GDD. Set in M6; the fire path already
    /// honours it so possession does not need to change these systems.
    /// </summary>
    public struct Possessed : IComponentData, IEnableableComponent { }

    public static class PossessionTuning
    {
        /// <summary>GDD: manual aim grants +30% fire rate.</summary>
        public const float FireRateBonus = 1.3f;
    }
}
