using Unity.Entities;
using Unity.Mathematics;

namespace ZeroSynth
{
    public enum PossessionMode : byte
    {
        None = 0,

        /// <summary>Static turret. Drag aims it; the camera does not move.</summary>
        Turret = 1,

        /// <summary>Goliath mech. Drag drives it; the camera follows and the quality profile changes.</summary>
        Mech = 2,

        /// <summary>HK gunship. Drag flies it over the horde; it runs on fuel and returns to its pad.</summary>
        Gunship = 3,
    }

    /// <summary>
    /// What the player is currently driving, and the input driving it.
    /// </summary>
    /// <remarks>
    /// Possession deliberately does not change the simulation's shape. The
    /// possessed entity keeps its archetype; flags select who drives it.
    /// Nothing is created, destroyed or re-archetyped when control changes.
    /// </remarks>
    public struct PossessionState : IComponentData
    {
        /// <summary>Entity.Null when nothing is possessed.</summary>
        public Entity Current;

        public PossessionMode Mode;

        /// <summary>World point the player is aiming at, on the ground plane.</summary>
        public float3 AimPoint;

        /// <summary>False until the player has aimed at least once.</summary>
        public bool HasAim;

        /// <summary>Virtual stick, in camera-relative XZ. Zero when not driving.</summary>
        public float2 MoveInput;
    }

    /// <summary>
    /// The player is aiming this weapon by hand, so auto-targeting must not.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Possessed"/> on purpose. A possessed turret is
    /// hand-aimed, but a possessed mech is driven while its weapon keeps
    /// auto-targeting — the player has their hands full steering. Keying the
    /// auto-targeting exclusion on this rather than on possession is what lets
    /// both work without special cases.
    /// </remarks>
    public struct ManualAim : IComponentData, IEnableableComponent { }

    /// <summary>
    /// A walker the player can drive. Weapons come from the ordinary turret
    /// components, so a mech is a turret that moves and nothing in the combat
    /// path needs to know the difference.
    /// </summary>
    public struct MechUnit : IComponentData
    {
        public float MoveSpeed;

        /// <summary>Radians per second.</summary>
        public float TurnSpeed;
    }

    /// <summary>
    /// What can happen to the mech. Agents that stand next to it chew on its
    /// hull; at zero it is wrecked, hidden, and rebuilt at <see cref="Home"/>
    /// after <see cref="RespawnSeconds"/>.
    /// </summary>
    /// <remarks>
    /// Damage is taken directly by <c>MechVitalsSystem</c> from the agents
    /// around it rather than through the damage queue, the way the core takes
    /// it. <see cref="Armor"/> scales that damage down: the Goliath is a
    /// plug, not a glass cannon, and raw swarm damage would melt it.
    /// </remarks>
    public struct MechVitals : IComponentData
    {
        public float3 Home;

        /// <summary>Metres from the mech within which agents hurt it.</summary>
        public float ReachRadius;

        /// <summary>Fraction of incoming melee damage that gets through.</summary>
        public float Armor;

        public float RegenPerSecond;

        /// <summary>Seconds without damage before the hull starts to mend.</summary>
        public float RegenDelay;
        public float QuietTimer;

        public float RespawnSeconds;
        public float RespawnTimer;
        public bool Dead;
    }

    /// <summary>
    /// A flyer the player can take over for a limited sortie. Its guns are the
    /// ordinary turret components, enabled only while it is flown: parked, it
    /// is scenery.
    /// </summary>
    /// <remarks>
    /// It has no <c>Structure</c> and no <c>Health</c>, so nothing in the
    /// swarm can target it and it cannot block building. The cost of using it
    /// is fuel: it burns while flown, refills only on the pad, and flies itself
    /// home when released or empty.
    /// </remarks>
    public struct GunshipUnit : IComponentData
    {
        public float MoveSpeed;

        /// <summary>Radians per second.</summary>
        public float TurnSpeed;

        /// <summary>Home position on the ground plane (y is ignored).</summary>
        public float3 Pad;

        /// <summary>Height flown at, and height sat at when parked.</summary>
        public float FlightHeight;
        public float ParkedHeight;

        /// <summary>Furthest it may fly from the pad.</summary>
        public float MaxRange;

        public float Fuel;
        public float MaxFuel;
        public float BurnPerSecond;
        public float RefillPerSecond;

        /// <summary>Fuel needed to take off at all, so it cannot be flown in tiny hops.</summary>
        public float MinLaunchFuel;
    }
}
