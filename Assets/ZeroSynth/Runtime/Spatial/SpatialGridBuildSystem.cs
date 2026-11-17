using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace ZeroSynth
{
    /// <summary>
    /// Rebuilds the spatial grid once per frame, before anything queries it.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AgentSteeringSystem))]
    [UpdateBefore(typeof(TurretTargetingSystem))]
    public partial struct SpatialGridBuildSystem : ISystem
    {
        EntityQuery m_DataQuery;
        EntityQuery m_AgentQuery;

        public void OnCreate(ref SystemState state)
        {
            m_DataQuery = state.GetEntityQuery(ComponentType.ReadOnly<SpatialGridData>());

            // Underground agents are left out of the grid, which is what makes
            // them untargetable until they surface.
            m_AgentQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<HordeAgent, Active, LocalTransform>()
                .WithNone<Underground>()
                .Build(ref state);

            state.RequireForUpdate<SpatialGridConfig>();
            state.RequireForUpdate(m_DataQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            var grid = state.EntityManager.GetComponentObject<SpatialGridData>(
                m_DataQuery.GetSingletonEntity());
            var config = SystemAPI.GetSingleton<SpatialGridConfig>();

            int agentCount = m_AgentQuery.CalculateEntityCount();
            if (agentCount > grid.Capacity)
                agentCount = grid.Capacity; // hard cap; pool size should prevent this

            var scratchEntities = new NativeArray<Entity>(agentCount, Allocator.TempJob);
            var scratchPositions = new NativeArray<float3>(agentCount, Allocator.TempJob);

            var gather = new GatherAgentsJob
            {
                Entities = scratchEntities,
                Positions = scratchPositions,
                Capacity = agentCount
            }.ScheduleParallel(m_AgentQuery, state.Dependency);

            var build = new BuildBucketsJob
            {
                Config = config,
                SrcEntities = scratchEntities,
                SrcPositions = scratchPositions,
                CellStart = grid.CellStart,
                DstEntities = grid.Entities,
                DstPositions = grid.Positions,
                Count = grid.Count
            }.Schedule(gather);

            state.Dependency = JobHandle.CombineDependencies(
                scratchEntities.Dispose(build),
                scratchPositions.Dispose(build));

            // This job rewrites arrays that the turret systems read next. They
            // live on a managed singleton, so nothing orders the two; complete
            // before they schedule.
            state.CompleteDependency();
        }
    }

    [BurstCompile]
    public partial struct GatherAgentsJob : IJobEntity
    {
        public int Capacity;

        // Each invocation writes its own index, so the parallel-for range
        // check is not meaningful here.
        [NativeDisableParallelForRestriction] public NativeArray<Entity> Entities;
        [NativeDisableParallelForRestriction] public NativeArray<float3> Positions;

        public void Execute([EntityIndexInQuery] int index, Entity entity, in LocalTransform transform)
        {
            if (index >= Capacity) return;
            Entities[index] = entity;
            Positions[index] = transform.Position;
        }
    }

    /// <summary>
    /// Count, prefix-sum, scatter. Single-threaded by design: at a few
    /// thousand agents this is microseconds, and the serial version avoids
    /// the atomics a parallel counting sort would need.
    /// </summary>
    [BurstCompile]
    public struct BuildBucketsJob : IJob
    {
        public SpatialGridConfig Config;

        [ReadOnly] public NativeArray<Entity> SrcEntities;
        [ReadOnly] public NativeArray<float3> SrcPositions;

        public NativeArray<int> CellStart;
        public NativeArray<Entity> DstEntities;
        public NativeArray<float3> DstPositions;
        public NativeArray<int> Count;

        public void Execute()
        {
            int cells = Config.CellCount;
            int n = SrcEntities.Length;
            Count[0] = n;

            for (int i = 0; i <= cells; i++)
                CellStart[i] = 0;

            // Count. Out-of-bounds agents fall into the clamped edge cell
            // rather than being dropped, so nothing silently vanishes.
            for (int i = 0; i < n; i++)
            {
                int c = ClampedCell(SrcPositions[i]);
                CellStart[c + 1]++;
            }

            for (int i = 0; i < cells; i++)
                CellStart[i + 1] += CellStart[i];

            var cursor = new NativeArray<int>(cells, Allocator.Temp);
            for (int i = 0; i < cells; i++)
                cursor[i] = CellStart[i];

            for (int i = 0; i < n; i++)
            {
                int c = ClampedCell(SrcPositions[i]);
                int slot = cursor[c]++;
                DstEntities[slot] = SrcEntities[i];
                DstPositions[slot] = SrcPositions[i];
            }

            cursor.Dispose();
        }

        int ClampedCell(float3 position)
        {
            int2 c = Config.ToCell(position);
            c = math.clamp(c, int2.zero, new int2(Config.Width - 1, Config.Height - 1));
            return Config.Index(c.x, c.y);
        }
    }
}
