using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace ZeroSynth
{
    /// <summary>
    /// The single place an agent's heading is decided. Everything else that
    /// influences movement — separation, avoidance, knockback — composes on
    /// top of the value this produces, so pathing can be retuned without
    /// touching any other system.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct AgentSteeringSystem : ISystem
    {
        EntityQuery m_GridQuery;

        public void OnCreate(ref SystemState state)
        {
            m_GridQuery = state.GetEntityQuery(ComponentType.ReadOnly<GridData>());
            state.RequireForUpdate<HordeTarget>();
            state.RequireForUpdate<HordeAgent>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var target = SystemAPI.GetSingleton<HordeTarget>();

            // No grid in the scene: fall back to steering straight at the
            // objective, so a scene without M1 content still behaves.
            if (m_GridQuery.IsEmpty)
            {
                state.Dependency = new DirectSteerJob
                {
                    DeltaTime = SystemAPI.Time.DeltaTime,
                    Goal = target.Position
                }.ScheduleParallel(state.Dependency);
                return;
            }

            var grid = state.EntityManager.GetComponentObject<GridData>(m_GridQuery.GetSingletonEntity());

            state.Dependency = new FlowFieldSteerJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                Config = SystemAPI.GetSingleton<GridConfig>(),
                Direction = grid.Direction,
                Goal = target.Position
            }.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct FlowFieldSteerJob : IJobEntity
    {
        public float DeltaTime;
        public GridConfig Config;
        public float3 Goal;

        [ReadOnly] public NativeArray<float2> Direction;

        public void Execute(ref LocalTransform transform, in HordeAgent agent)
        {
            float3 heading = float3.zero;

            int2 cell = Config.WorldToCell(transform.Position);
            if (Config.InBounds(cell))
            {
                float2 d = Direction[Config.Index(cell)];
                if (math.lengthsq(d) > 1e-6f)
                    heading = new float3(d.x, 0f, d.y);
            }

            // Zero heading means either the goal cell itself or an unreachable
            // pocket. Both are handled by aiming at the objective directly:
            // in the goal cell it converges on the exact point instead of the
            // cell centre, and in a pocket it at least presses toward it.
            if (math.lengthsq(heading) < 1e-6f)
            {
                float3 toGoal = Goal - transform.Position;
                toGoal.y = 0f;
                float distSq = math.lengthsq(toGoal);
                if (distSq < 1e-4f)
                    return;
                heading = toGoal * math.rsqrt(distSq);
            }

            transform.Position += heading * agent.Speed * DeltaTime;
            transform.Rotation = quaternion.LookRotationSafe(heading, math.up());
        }
    }

    [BurstCompile]
    public partial struct DirectSteerJob : IJobEntity
    {
        public float DeltaTime;
        public float3 Goal;

        public void Execute(ref LocalTransform transform, in HordeAgent agent)
        {
            float3 toGoal = Goal - transform.Position;
            toGoal.y = 0f;

            float distSq = math.lengthsq(toGoal);
            if (distSq < 1e-4f)
                return;

            float3 heading = toGoal * math.rsqrt(distSq);
            transform.Position += heading * agent.Speed * DeltaTime;
            transform.Rotation = quaternion.LookRotationSafe(heading, math.up());
        }
    }
}
