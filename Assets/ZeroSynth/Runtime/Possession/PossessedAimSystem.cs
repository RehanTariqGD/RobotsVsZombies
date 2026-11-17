using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace ZeroSynth
{
    /// <summary>
    /// Targets possessed turrets from the player's aim point instead of the
    /// stance rules.
    /// </summary>
    /// <remarks>
    /// Sits between auto-targeting and firing. Auto-targeting excludes
    /// possessed turrets, this fills in their target, and the fire path is
    /// identical for both — which is why possession needed no changes to
    /// damage, cooldowns or the power gate.
    ///
    /// Manual aim selects the agent nearest the aim point rather than
    /// requiring a precise hit. On a touchscreen a reticle you must land on a
    /// moving 40-triangle zombie is not playable; "shoot into this crowd" is.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TurretTargetingSystem))]
    [UpdateBefore(typeof(TurretFireSystem))]
    public partial struct PossessedAimSystem : ISystem
    {
        EntityQuery m_DataQuery;

        public void OnCreate(ref SystemState state)
        {
            m_DataQuery = state.GetEntityQuery(ComponentType.ReadOnly<SpatialGridData>());
            state.RequireForUpdate<SpatialGridConfig>();
            state.RequireForUpdate<PossessionState>();
            state.RequireForUpdate(m_DataQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            var possession = SystemAPI.GetSingleton<PossessionState>();
            if (possession.Mode != PossessionMode.Turret || !possession.HasAim)
                return;

            var grid = state.EntityManager.GetComponentObject<SpatialGridData>(
                m_DataQuery.GetSingletonEntity());

            state.Dependency = new AimTargetJob
            {
                Config = SystemAPI.GetSingleton<SpatialGridConfig>(),
                CellStart = grid.CellStart,
                Entities = grid.Entities,
                Positions = grid.Positions,
                AimPoint = possession.AimPoint
            }.ScheduleParallel(state.Dependency);

            state.CompleteDependency();
        }
    }

    [BurstCompile]
    [WithAll(typeof(Powered))]
    [WithAll(typeof(ManualAim))]
    public partial struct AimTargetJob : IJobEntity
    {
        public SpatialGridConfig Config;
        public float3 AimPoint;

        [ReadOnly] public NativeArray<int> CellStart;
        [ReadOnly] public NativeArray<Entity> Entities;
        [ReadOnly] public NativeArray<float3> Positions;

        public void Execute(ref TurretTarget target, in Turret turret, in LocalTransform transform)
        {
            float3 origin = transform.Position;
            float rangeSq = turret.Range * turret.Range;

            // Search around the aim point, not the turret: the player is
            // pointing at a crowd, and the turret's own range is then a
            // separate validity check.
            int span = (int)math.ceil(turret.Range / Config.CellSize);
            int2 centre = Config.ToCell(AimPoint);

            Entity best = Entity.Null;
            float bestDistToAim = float.MaxValue;

            for (int y = centre.y - span; y <= centre.y + span; y++)
            for (int x = centre.x - span; x <= centre.x + span; x++)
            {
                if (!Config.InBounds(x, y)) continue;

                int cell = Config.Index(x, y);
                for (int i = CellStart[cell]; i < CellStart[cell + 1]; i++)
                {
                    float3 position = Positions[i];

                    float3 fromTurret = position - origin;
                    fromTurret.y = 0f;
                    if (math.lengthsq(fromTurret) > rangeSq) continue;

                    float3 fromAim = position - AimPoint;
                    fromAim.y = 0f;
                    float d = math.lengthsq(fromAim);

                    if (d < bestDistToAim)
                    {
                        bestDistToAim = d;
                        best = Entities[i];
                    }
                }
            }

            target.Value = best;
        }
    }
}
