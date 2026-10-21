using Unity.Entities;

namespace ZeroSynth
{
    /// <summary>
    /// Participation in the power network.
    /// </summary>
    /// <remarks>
    /// One rule covers reactors, pylons and consumers: a node is powered if it
    /// is a source, or if it sits inside the supply range of something already
    /// powered. Relays carry the network outward; consumers sit on the end of
    /// it with a supply range of zero.
    /// </remarks>
    public struct PowerNode : IComponentData
    {
        /// <summary>
        /// How far this node carries power onward. Zero means it consumes but
        /// does not relay, so the network cannot route through a turret.
        /// </summary>
        public float SupplyRange;

        /// <summary>Reactor. Powered unconditionally while alive.</summary>
        public bool IsSource;
    }

    /// <summary>
    /// Set by the power flood. Enableable, so systems that query on it skip
    /// unpowered structures entirely rather than testing a flag per entity —
    /// an unpowered turret costs nothing at all.
    /// </summary>
    public struct Powered : IComponentData, IEnableableComponent { }
}
