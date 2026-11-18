using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace ZeroSynth
{
    /// <summary>
    /// Moves tunnelling agents straight at the objective, below the ground,
    /// and surfaces them when they get close.
    /// </summary>
    /// <remarks>
    /// Underground agents ignore the flow field entirely, so walls and terrain
    /// do nothing to them. They are also excluded from the spatial grid, which
    /// is what makes them untargetable until they surface. Surfacing is an
    /// enable-bit flip through a command buffer — no structural change.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(WaveDirectorSystem))]
    [UpdateBefore(typeof(AgentSteeringSystem))]
    public partial struct BurrowSystem : ISystem
    {
        EntityQuery m_FeedbackQuery;

        public void OnCreate(ref SystemState state)
        {
            m_FeedbackQuery = state.GetEntityQuery(ComponentType.ReadOnly<SimFeedback>());
            state.RequireForUpdate(m_FeedbackQuery);
            state.RequireForUpdate<HordeTarget>();
            state.RequireForUpdate<SpawnProfile>();
            state.RequireForUpdate<SimClock>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var clock = SystemAPI.GetSingleton<SimClock>();
            if (clock.Paused)
                return;

            var profile = SystemAPI.GetSingleton<SpawnProfile>();
            var target = SystemAPI.GetSingleton<HordeTarget>();

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            var feedback = state.EntityManager.GetComponentObject<SimFeedback>(
                m_FeedbackQuery.GetSingletonEntity());

            state.Dependency = new BurrowJob
            {
                Dust = feedback.Dust.AsParallelWriter(),
                Frame = UnityEngine.Time.frameCount,
                DeltaTime = SystemAPI.Time.DeltaTime * math.max(0f, clock.Speed),
                Goal = target.Position,
                SurfaceRadiusSq = profile.BurrowSurfaceRadius * profile.BurrowSurfaceRadius,
                Depth = profile.BurrowDepth,
                SpeedMultiplier = profile.BurrowSpeedMultiplier,
                Commands = ecb.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);

            // The dust queue belongs to a managed singleton, so nothing orders
            // this job against the other writers; see TurretFireSystem.
            state.CompleteDependency();
        }
    }

    [BurstCompile]
    [WithAll(typeof(Active), typeof(Underground))]
    public partial struct BurrowJob : IJobEntity
    {
        public float DeltaTime;
        public float3 Goal;
        public float SurfaceRadiusSq;
        public float Depth;
        public float SpeedMultiplier;

        public EntityCommandBuffer.ParallelWriter Commands;
        public NativeQueue<DustEvent>.ParallelWriter Dust;

        /// <summary>Frame number, used to space each agent's trail puffs apart.</summary>
        public int Frame;

        /// <summary>One puff per agent every this many frames. Presentation only.</summary>
        const int TrailInterval = 10;

        public void Execute(
            [ChunkIndexInQuery] int chunkIndex,
            Entity entity,
            ref LocalTransform transform,
            in HordeAgent agent)
        {
            float3 toGoal = Goal - transform.Position;
            toGoal.y = 0f;

            float distSq = math.lengthsq(toGoal);

            if (distSq <= SurfaceRadiusSq)
            {
                transform.Position.y = 0f;
                Commands.SetComponentEnabled<Underground>(chunkIndex, entity, false);
                Dust.Enqueue(new DustEvent { Position = transform.Position, Surfacing = true });
                return;
            }

            float3 heading = toGoal * math.rsqrt(distSq);
            transform.Position += heading * agent.Speed * SpeedMultiplier * DeltaTime;
            transform.Position.y = Depth;

            // A churned-up line of ground over the tunnel, so the player can
            // see where they are coming from before they surface.
            if ((entity.Index + Frame) % TrailInterval == 0)
                Dust.Enqueue(new DustEvent
                {
                    Position = new float3(transform.Position.x, 0f, transform.Position.z),
                    Surfacing = false
                });
            transform.Rotation = quaternion.LookRotationSafe(heading, math.up());
        }
    }
}
