using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace ZeroSynth
{
    /// <summary>
    /// Steers every agent at the shared target. Placeholder steering: this is
    /// where the flow-field lookup will go, and it is intentionally the only
    /// place that decides direction so that swap is a one-file change.
    /// </summary>
    [BurstCompile]
    public partial struct HordeMoveJob : IJobEntity
    {
        public float DeltaTime;
        public float3 TargetPosition;

        public void Execute(ref LocalTransform transform, in HordeAgent agent)
        {
            float3 toTarget = TargetPosition - transform.Position;

            // Squared length first so we can reject and normalise without a
            // second sqrt, and avoid a divide-by-zero at the target.
            float distanceSq = math.lengthsq(toTarget);
            if (distanceSq < 0.0001f)
                return;

            float3 direction = toTarget * math.rsqrt(distanceSq);

            transform.Position += direction * agent.Speed * DeltaTime;
            transform.Rotation = quaternion.LookRotationSafe(direction, math.up());
        }
    }

    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct HordeMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<HordeTarget>();
            state.RequireForUpdate<HordeAgent>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var target = SystemAPI.GetSingleton<HordeTarget>();

            new HordeMoveJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                TargetPosition = target.Position
            }.ScheduleParallel();
        }
    }
}
