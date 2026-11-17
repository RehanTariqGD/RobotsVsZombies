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

            state.CompleteDependency();
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
            // Floored at zero so time spent walking is not banked as attacks.
            combat.Cooldown = math.max(0f, combat.Cooldown - DeltaTime);
            if (combat.Cooldown > 0f || combat.AttackRate <= 0f) return;

            int2 cell = Config.WorldToCell(transform.Position);
            if (!Config.InBounds(cell)) return;

            float2 heading = Direction[Config.Index(cell)];
            if (math.lengthsq(heading) < 1e-6f) return;

            var wall = FindWall(transform.Position, heading, combat.Reach);
            if (wall == Entity.Null) return;

            combat.Cooldown = 1f / combat.AttackRate;
            Writer.Enqueue(new DamageEvent { Target = wall, Amount = combat.Damage });
        }

        /// <summary>
        /// First wall along the heading within reach.
        /// </summary>
        /// <remarks>
        /// Marches in half-cell steps rather than probing the single point at full
        /// reach. An agent held against a one-cell wall stops a hair short of its
        /// face, so a point 1.2 m ahead lands on the far side of it, finds
        /// nothing, and the agent would stand there never attacking. Longer
        /// reach, such as the Titan's, made it worse.
        /// </remarks>
        Entity FindWall(float3 from, float2 heading, float reach)
        {
            float step = Config.CellSize * 0.5f;
            int steps = (int)math.ceil(reach / step);
            float3 direction = new float3(heading.x, 0f, heading.y);

            for (int i = 0; i <= steps; i++)
            {
                float3 probe = from + direction * math.min(i * step, reach);
                int2 cell = Config.WorldToCell(probe);
                if (!Config.InBounds(cell)) return Entity.Null;

                int index = Config.Index(cell);
                var flags = Flags[index];

                if ((flags & CellFlags.Impassable) != 0) return Entity.Null;
                if ((flags & CellFlags.Wall) != 0) return Occupant[index];
            }

            return Entity.Null;
        }
    }
}
