using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace ZeroSynth
{
    /// <summary>
    /// Compile-only validation that the DOTS toolchain resolves end to end:
    /// Entities, Burst, Collections, Mathematics and Transforms.
    /// Carries no gameplay meaning. Safe to delete once real systems exist.
    /// </summary>
    public struct ToolchainProbe : IComponentData
    {
        public float3 Velocity;
    }

    [BurstCompile]
    public partial struct ToolchainProbeJob : IJobEntity
    {
        public float DeltaTime;

        public void Execute(ref LocalTransform transform, in ToolchainProbe probe)
        {
            transform.Position += probe.Velocity * DeltaTime;
        }
    }

    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    public partial struct ToolchainSmokeTestSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ToolchainProbe>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Exercises Collections + Mathematics inside a Burst context.
            var scratch = new NativeArray<float3>(4, Allocator.Temp);
            for (int i = 0; i < scratch.Length; i++)
                scratch[i] = math.normalizesafe(new float3(i + 1, 0f, 1f));
            scratch.Dispose();

            new ToolchainProbeJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime
            }.ScheduleParallel();
        }
    }
}
