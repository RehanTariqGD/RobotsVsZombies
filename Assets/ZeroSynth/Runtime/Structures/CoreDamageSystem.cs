using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace ZeroSynth
{
    /// <summary>
    /// Agents that reach the core chew on it.
    /// </summary>
    /// <remarks>
    /// Walls are attacked cell by cell through the occupancy array; the core is
    /// a single entity at the objective, where the flow field ends, so it is
    /// cheaper to ask the spatial grid who is standing next to it. The damage
    /// is summed into one event per frame instead of one per agent.
    ///
    /// Only melee damage is counted. Spitters hurt the core the same way they
    /// hurt everything else: it is a Structure, so their ranged targeting
    /// picks it up.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SpatialGridBuildSystem))]
    [UpdateBefore(typeof(DamageApplySystem))]
    public partial struct CoreDamageSystem : ISystem
    {
        /// <summary>Metres from the core's centre within which an agent can bite it.</summary>
        const float ReachRadius = 3.5f;

        EntityQuery m_CoreQuery;
        EntityQuery m_GridQuery;
        EntityQuery m_PathGridQuery;
        EntityQuery m_BufferQuery;

        public void OnCreate(ref SystemState state)
        {
            m_CoreQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Core, Health, LocalTransform>()
                .Build(ref state);
            m_GridQuery = state.GetEntityQuery(ComponentType.ReadOnly<SpatialGridData>());
            m_PathGridQuery = state.GetEntityQuery(ComponentType.ReadOnly<GridData>());
            m_BufferQuery = state.GetEntityQuery(ComponentType.ReadOnly<DamageBuffer>());

            state.RequireForUpdate(m_CoreQuery);
            state.RequireForUpdate(m_GridQuery);
            state.RequireForUpdate(m_PathGridQuery);
            state.RequireForUpdate(m_BufferQuery);
            state.RequireForUpdate<SpatialGridConfig>();
            state.RequireForUpdate<GridConfig>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var core = m_CoreQuery.GetSingletonEntity();
            if (SystemAPI.GetComponent<Health>(core).Current <= 0f)
                return;

            var grid = state.EntityManager.GetComponentObject<SpatialGridData>(
                m_GridQuery.GetSingletonEntity());
            var buffer = state.EntityManager.GetComponentObject<DamageBuffer>(
                m_BufferQuery.GetSingletonEntity());
            var pathGrid = state.EntityManager.GetComponentObject<GridData>(
                m_PathGridQuery.GetSingletonEntity());

            state.Dependency = new CoreDamageJob
            {
                Core = core,
                CorePosition = SystemAPI.GetComponent<LocalTransform>(core).Position,
                Radius = ReachRadius,
                DeltaTime = SystemAPI.Time.DeltaTime,
                PathConfig = SystemAPI.GetSingleton<GridConfig>(),
                PathFlags = pathGrid.Flags,
                Config = SystemAPI.GetSingleton<SpatialGridConfig>(),
                CellStart = grid.CellStart,
                Entities = grid.Entities,
                Positions = grid.Positions,
                Combat = SystemAPI.GetComponentLookup<AgentCombat>(true),
                Queue = buffer.Pending
            }.Schedule(state.Dependency);

            // Writes the shared damage queue; see TurretFireSystem.
            state.CompleteDependency();
        }
    }

    [BurstCompile]
    public struct CoreDamageJob : IJob
    {
        public Entity Core;
        public float3 CorePosition;
        public float Radius;
        public float DeltaTime;
        public SpatialGridConfig Config;
        public GridConfig PathConfig;

        [ReadOnly] public NativeArray<int> CellStart;
        [ReadOnly] public NativeArray<Entity> Entities;
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<CellFlags> PathFlags;
        [ReadOnly] public ComponentLookup<AgentCombat> Combat;

        public NativeQueue<DamageEvent> Queue;

        /// <summary>
        /// True if a wall stands between the agent and the core. Without this a
        /// ring of barricades does nothing for the core: everything queued
        /// outside it but inside the radius would still be biting through it.
        /// </summary>
        bool WallBetween(float3 from)
        {
            float3 toCore = CorePosition - from;
            toCore.y = 0f;

            float length = math.length(toCore);
            if (length < 1e-3f) return false;

            float3 direction = toCore / length;
            float step = PathConfig.CellSize * 0.5f;

            for (float d = 0f; d < length; d += step)
            {
                int2 cell = PathConfig.WorldToCell(from + direction * d);
                if (!PathConfig.InBounds(cell)) continue;
                if ((PathFlags[PathConfig.Index(cell)] & CellFlags.Wall) != 0)
                    return true;
            }

            return false;
        }

        public void Execute()
        {
            float radiusSq = Radius * Radius;
            int span = (int)math.ceil(Radius / Config.CellSize);
            int2 centre = Config.ToCell(CorePosition);

            float damagePerSecond = 0f;

            for (int y = centre.y - span; y <= centre.y + span; y++)
            for (int x = centre.x - span; x <= centre.x + span; x++)
            {
                if (!Config.InBounds(x, y)) continue;

                int cell = Config.Index(x, y);
                for (int i = CellStart[cell]; i < CellStart[cell + 1]; i++)
                {
                    float3 delta = Positions[i] - CorePosition;
                    delta.y = 0f;
                    if (math.lengthsq(delta) > radiusSq) continue;

                    var agent = Entities[i];
                    if (!Combat.HasComponent(agent)) continue;
                    if (WallBetween(Positions[i])) continue;

                    var combat = Combat[agent];
                    damagePerSecond += combat.Damage * combat.AttackRate;
                }
            }

            float amount = damagePerSecond * DeltaTime;
            if (amount > 0f)
                Queue.Enqueue(new DamageEvent { Target = Core, Amount = amount });
        }
    }
}
