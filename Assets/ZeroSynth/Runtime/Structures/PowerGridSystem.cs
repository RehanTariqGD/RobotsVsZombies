using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace ZeroSynth
{
    /// <summary>
    /// Floods power outward from reactors and toggles <see cref="Powered"/>.
    /// </summary>
    /// <remarks>
    /// A graph flood, not a simulation. Structure counts are in the hundreds,
    /// so this is microseconds on the main thread and runs only when the node
    /// set actually changes — a node built, destroyed, or moved.
    ///
    /// The GDD's "destroyed nodes drop surrounding defences offline" needs no
    /// special handling: a dead relay simply stops appearing in the flood, and
    /// everything downstream of it fails to be reached.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TurretTargetingSystem))]
    public partial struct PowerGridSystem : ISystem
    {
        EntityQuery m_NodeQuery;
        uint m_Signature;
        bool m_Initialised;

        public void OnCreate(ref SystemState state)
        {
            m_NodeQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PowerNode, LocalTransform, Powered>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(ref state);

            state.RequireForUpdate(m_NodeQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            var nodes = m_NodeQuery.ToComponentDataArray<PowerNode>(Allocator.Temp);
            var transforms = m_NodeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var entities = m_NodeQuery.ToEntityArray(Allocator.Temp);

            var healthLookup = SystemAPI.GetComponentLookup<Health>(true);

            var alive = new NativeArray<bool>(entities.Length, Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                alive[i] = !healthLookup.HasComponent(entities[i])
                           || healthLookup[entities[i]].Current > 0f;
            }

            uint signature = Signature(nodes, transforms, alive);
            if (m_Initialised && signature == m_Signature)
            {
                nodes.Dispose(); transforms.Dispose(); entities.Dispose(); alive.Dispose();
                return;
            }

            state.CompleteDependency();

            var powered = new NativeArray<bool>(entities.Length, Allocator.Temp);
            var frontier = new NativeList<int>(entities.Length, Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (!alive[i] || !nodes[i].IsSource) continue;
                powered[i] = true;
                frontier.Add(i);
            }

            // Breadth-first outward. Only relays extend the frontier, so a
            // consumer cannot be used as a stepping stone.
            while (frontier.Length > 0)
            {
                int current = frontier[frontier.Length - 1];
                frontier.RemoveAt(frontier.Length - 1);

                float range = nodes[current].SupplyRange;
                if (range <= 0f) continue;

                float rangeSq = range * range;
                float3 origin = transforms[current].Position;

                for (int i = 0; i < entities.Length; i++)
                {
                    if (powered[i] || !alive[i]) continue;

                    float3 delta = transforms[i].Position - origin;
                    if (math.lengthsq(delta) > rangeSq) continue;

                    powered[i] = true;
                    frontier.Add(i);
                }
            }

            var em = state.EntityManager;
            for (int i = 0; i < entities.Length; i++)
            {
                bool shouldBePowered = powered[i] && alive[i];
                if (em.IsComponentEnabled<Powered>(entities[i]) != shouldBePowered)
                    em.SetComponentEnabled<Powered>(entities[i], shouldBePowered);
            }

            powered.Dispose();
            frontier.Dispose();
            nodes.Dispose();
            transforms.Dispose();
            entities.Dispose();
            alive.Dispose();

            m_Signature = signature;
            m_Initialised = true;
        }

        static uint Signature(
            NativeArray<PowerNode> nodes,
            NativeArray<LocalTransform> transforms,
            NativeArray<bool> alive)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < nodes.Length; i++)
            {
                hash = (hash ^ math.hash(transforms[i].Position)) * 16777619u;
                hash = (hash ^ math.hash(new float2(nodes[i].SupplyRange, nodes[i].IsSource ? 1f : 0f))) * 16777619u;
                hash = (hash ^ (alive[i] ? 1u : 0u)) * 16777619u;
            }
            return hash ^ (uint)nodes.Length;
        }
    }
}
