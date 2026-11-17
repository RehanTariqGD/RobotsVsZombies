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
        EntityQuery m_BufferQuery;

        public void OnCreate(ref SystemState state)
        {
            m_CoreQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Core, Health, LocalTransform>()
                .Build(ref state);
            m_GridQuery = state.GetEntityQuery(ComponentType.ReadOnly<SpatialGridData>());
            m_BufferQuery = state.GetEntityQuery(ComponentType.ReadOnly<DamageBuffer>());

            state.RequireForUpdate(m_CoreQuery);
            state.RequireForUpdate(m_GridQuery);
            state.RequireForUpdate(m_BufferQuery);
            state.RequireForUpdate<SpatialGridConfig>();
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

            state.Dependency = new CoreDamageJob
            {
                Core = core,
                CorePosition = SystemAPI.GetComponent<LocalTransform>(core).Position,
                Radius = ReachRadius,
                DeltaTime = SystemAPI.Time.DeltaTime,
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

        [ReadOnly] public NativeArray<int> CellStart;
        [ReadOnly] public NativeArray<Entity> Entities;
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public ComponentLookup<AgentCombat> Combat;

        public NativeQueue<DamageEvent> Queue;

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
