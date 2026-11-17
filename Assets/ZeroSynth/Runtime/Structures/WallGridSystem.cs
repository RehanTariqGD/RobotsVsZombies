using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ZeroSynth
{
    /// <summary>
    /// Keeps the grid's cost and occupancy in step with live structure health,
    /// and dirties the flow field only when it genuinely changes.
    /// </summary>
    /// <remarks>
    /// This is the trick behind the genre's signature behaviour. Walls are not
    /// impassable — they carry a traversal cost scaled to remaining health, so
    /// the integration sweep funnels the swarm at the weakest segment on its
    /// own and repairing a wall visibly re-routes it. No wall-seeking AI exists
    /// anywhere in the project.
    ///
    /// Cost is quantised into a few health bands rather than tracking health
    /// continuously. A wall under attack loses health every frame, and keying
    /// the field rebuild off the raw value would re-run the Dijkstra sweep
    /// every frame for the entire siege. Banding means a wall triggers a
    /// handful of rebuilds over its whole lifetime.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(FlowFieldSystem))]
    public partial struct WallGridSystem : ISystem
    {
        /// <summary>Health bands. More bands means finer routing, more rebuilds.</summary>
        const int Bands = 8;

        EntityQuery m_GridQuery;
        EntityQuery m_WallQuery;

        /// <summary>Cells written last pass, so clearing does not scan the whole grid.</summary>
        NativeList<int> m_Written;

        uint m_Signature;
        bool m_Initialised;

        public void OnCreate(ref SystemState state)
        {
            m_GridQuery = state.GetEntityQuery(ComponentType.ReadOnly<GridData>());

            m_WallQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<GridAnchor, Structure, Health>()
                .Build(ref state);

            m_Written = new NativeList<int>(256, Allocator.Persistent);

            state.RequireForUpdate<GridConfig>();
            state.RequireForUpdate(m_GridQuery);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (m_Written.IsCreated) m_Written.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<GridConfig>();

            var anchors = m_WallQuery.ToComponentDataArray<GridAnchor>(Allocator.Temp);
            var structures = m_WallQuery.ToComponentDataArray<Structure>(Allocator.Temp);
            var healths = m_WallQuery.ToComponentDataArray<Health>(Allocator.Temp);

            uint signature = Signature(anchors, structures, healths);

            if (m_Initialised && signature == m_Signature)
            {
                anchors.Dispose();
                structures.Dispose();
                healths.Dispose();
                return;
            }

            state.CompleteDependency();

            var grid = state.EntityManager.GetComponentObject<GridData>(
                m_GridQuery.GetSingletonEntity());
            var entities = m_WallQuery.ToEntityArray(Allocator.Temp);

            // Clear only what the previous pass touched.
            for (int i = 0; i < m_Written.Length; i++)
            {
                int index = m_Written[i];
                grid.Flags[index] &= ~CellFlags.Wall;
                grid.ExtraCost[index] = 0f;
                grid.Occupant[index] = Entity.Null;
            }
            m_Written.Clear();

            for (int i = 0; i < entities.Length; i++)
            {
                var health = healths[i];
                if (health.Current <= 0f)
                    continue;

                float cost = BandedCost(health);
                int2 size = math.max(structures[i].Footprint, new int2(1, 1));
                int2 origin = anchors[i].Cell;

                for (int y = 0; y < size.y; y++)
                for (int x = 0; x < size.x; x++)
                {
                    int cx = origin.x + x;
                    int cy = origin.y + y;
                    if (!config.InBounds(cx, cy)) continue;

                    int index = config.Index(cx, cy);
                    grid.Flags[index] |= CellFlags.Wall;
                    grid.ExtraCost[index] = cost;
                    grid.Occupant[index] = entities[i];
                    m_Written.Add(index);
                }
            }

            entities.Dispose();
            anchors.Dispose();
            structures.Dispose();
            healths.Dispose();

            m_Signature = signature;
            m_Initialised = true;
            grid.Dirty = true;
        }

        static int Band(Health health)
        {
            if (health.Max <= 0f) return Bands;
            float ratio = math.saturate(health.Current / health.Max);
            return (int)math.round(ratio * Bands);
        }

        static float BandedCost(Health health)
        {
            float ratio = (float)Band(health) / Bands;
            return math.max(WallTuning.MinCost, WallTuning.FullHealthCost * ratio);
        }

        /// <summary>
        /// Changes when a wall is added, removed, moved, or crosses a health
        /// band. Stable otherwise, which is what keeps the field from
        /// rebuilding during a siege.
        /// </summary>
        static uint Signature(
            NativeArray<GridAnchor> anchors,
            NativeArray<Structure> structures,
            NativeArray<Health> healths)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < anchors.Length; i++)
            {
                hash = (hash ^ (uint)anchors[i].Cell.x) * 16777619u;
                hash = (hash ^ (uint)anchors[i].Cell.y) * 16777619u;
                hash = (hash ^ (uint)structures[i].Footprint.x) * 16777619u;
                hash = (hash ^ (uint)structures[i].Footprint.y) * 16777619u;
                hash = (hash ^ (uint)Band(healths[i])) * 16777619u;
            }
            return hash ^ (uint)anchors.Length;
        }
    }
}
