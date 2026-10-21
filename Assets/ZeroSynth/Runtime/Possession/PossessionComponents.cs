using Unity.Entities;
using Unity.Mathematics;

namespace ZeroSynth
{
    /// <summary>
    /// What the player is currently driving, and where they are aiming it.
    /// </summary>
    /// <remarks>
    /// Possession deliberately does not change the simulation's shape. The
    /// possessed entity is an ordinary turret with <see cref="Possessed"/>
    /// enabled; auto-targeting skips it and <see cref="PossessedAimSystem"/>
    /// drives it instead. Nothing is created, destroyed or re-archetyped when
    /// the player takes or releases control.
    /// </remarks>
    public struct PossessionState : IComponentData
    {
        /// <summary>Entity.Null when nothing is possessed.</summary>
        public Entity Current;

        /// <summary>World point the player is aiming at, on the ground plane.</summary>
        public float3 AimPoint;

        /// <summary>False until the player has aimed at least once.</summary>
        public bool HasAim;
    }
}
