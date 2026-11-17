using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace ZeroSynth
{
    /// <summary>
    /// Agents chew through whatever wall stands between them and the
    /// objective.
    /// </summary>
    /// <remarks>
    /// There is no target selection here. The flow field already pointed the
    /// agent at the cheapest route; if a wall occupies the cell it is walking
    /// into, that wall is the thing in the way, and the occupancy array hands
    /// it over without any spatial query. "Attack the weakest point" is an
    /// emergent consequence of the cost field, not a decision.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AgentSteeringSystem))]
    [UpdateBefore(typeof(DamageApplySystem))]
    public partial struct AgentAttackSystem : ISystem
    {
        EntityQuery m_GridQuery;
        EntityQuery m_BufferQuery;

        public void OnCreate(ref SystemState state)
        {
            m_GridQuery = state.GetEntityQuery(ComponentType.ReadOnly<GridData>());
            m_BufferQuery = state.GetEntityQuery(ComponentType.ReadOnly<DamageBuffer>());

            state.RequireForUpdate<GridConfig>();
            state.RequireForUpdate<AgentCombat>();
            state.RequireForUpdate(m_GridQuery);
            state.RequireForUpdate(m_BufferQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            var grid = state.EntityManager.GetComponentObject<GridData>(
                m_GridQuery.GetSingletonEntity());
            var buffer = state.EntityManager.GetComponentObject<DamageBuffer>(
                m_BufferQuery.GetSingletonEntity());

            state.Dependency = new AttackWallJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                Config = SystemAPI.GetSingleton<GridConfig>(),
                Flags = grid.Flags,
                Occupant = grid.Occupant,
                Direction = grid.Direction,
                Writer = buffer.Pending.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    [WithAll(typeof(Active))]
    [WithNone(typeof(Underground))]
    public partial struct AttackWallJob : IJobEntity
    {
        public float DeltaTime;
        public GridConfig Config;

        [ReadOnly] public NativeArray<CellFlags> Flags;
        [ReadOnly] public NativeArray<Entity> Occupant;
        [ReadOnly] public NativeArray<float2> Direction;

        public NativeQueue<DamageEvent>.ParallelWriter Writer;

        public void Execute(ref AgentCombat combat, in LocalTransform transform)
        {
            combat.Cooldown -= DeltaTime;

            int2 cell = Config.WorldToCell(transform.Position);
            if (!Config.InBounds(cell)) return;

            float2 heading = Direction[Config.Index(cell)];
            if (math.lengthsq(heading) < 1e-6f) return;

            // Probe the cell the agent is heading into, at its reach.
            float3 probe = transform.Position + new float3(heading.x, 0f, heading.y) * combat.Reach;
            int2 target = Config.WorldToCell(probe);
            if (!Config.InBounds(target)) return;

            int index = Config.Index(target);
            if ((Flags[index] & CellFlags.Wall) == 0) return;

            var wall = Occupant[index];
            if (wall == Entity.Null) return;

            if (combat.Cooldown > 0f || combat.AttackRate <= 0f) return;

            combat.Cooldown += 1f / combat.AttackRate;
            Writer.Enqueue(new DamageEvent { Target = wall, Amount = combat.Damage });
        }
    }
}
