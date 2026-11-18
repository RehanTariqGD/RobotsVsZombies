using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace ZeroSynth
{
    /// <summary>
    /// Hurts, mends, wrecks and rebuilds the mech.
    /// </summary>
    /// <remarks>
    /// Runs on the main thread: there is one mech, and the work includes
    /// structural changes (hiding the wreck, showing the rebuild), which would
    /// otherwise need a command buffer for no gain. The spatial grid's arrays
    /// are safe to read here because the grid build completes its own job.
    ///
    /// A wrecked mech is hidden with <see cref="DisableRendering"/> and has
    /// <see cref="Powered"/> off so its guns stop. If the player was driving
    /// it, the possession is cleared here and the controller lets go on its
    /// next frame.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SpatialGridBuildSystem))]
    [UpdateBefore(typeof(DamageApplySystem))]
    public partial struct MechVitalsSystem : ISystem
    {
        EntityQuery m_MechQuery;
        EntityQuery m_GridQuery;

        public void OnCreate(ref SystemState state)
        {
            m_MechQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<MechUnit, MechVitals, Health, LocalTransform>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(ref state);
            m_GridQuery = state.GetEntityQuery(ComponentType.ReadOnly<SpatialGridData>());

            state.RequireForUpdate(m_MechQuery);
            state.RequireForUpdate(m_GridQuery);
            state.RequireForUpdate<SpatialGridConfig>();
            state.RequireForUpdate<PossessionState>();
            state.RequireForUpdate<SimClock>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var clock = SystemAPI.GetSingleton<SimClock>();
            if (clock.Paused)
                return;

            float dt = SystemAPI.Time.DeltaTime * math.max(0f, clock.Speed);
            var em = state.EntityManager;

            var grid = em.GetComponentObject<SpatialGridData>(m_GridQuery.GetSingletonEntity());
            var config = SystemAPI.GetSingleton<SpatialGridConfig>();
            var possessionEntity = SystemAPI.GetSingletonEntity<PossessionState>();

            var mechs = m_MechQuery.ToEntityArray(Allocator.Temp);

            for (int m = 0; m < mechs.Length; m++)
            {
                var mech = mechs[m];
                var vitals = em.GetComponentData<MechVitals>(mech);
                var health = em.GetComponentData<Health>(mech);
                var transform = em.GetComponentData<LocalTransform>(mech);

                if (vitals.Dead)
                {
                    vitals.RespawnTimer -= dt;
                    if (vitals.RespawnTimer <= 0f)
                    {
                        vitals.Dead = false;
                        vitals.QuietTimer = 0f;
                        health.Current = health.Max;
                        transform.Position = vitals.Home;

                        em.RemoveComponent<DisableRendering>(mech);
                        em.SetComponentEnabled<Powered>(mech, true);

                        em.SetComponentData(mech, health);
                        em.SetComponentData(mech, transform);
                    }

                    em.SetComponentData(mech, vitals);
                    continue;
                }

                float incoming = IncomingDps(em, config, grid, transform.Position, vitals.ReachRadius);

                if (incoming > 0f)
                {
                    vitals.QuietTimer = 0f;
                    health.Current -= incoming * vitals.Armor * dt;
                }
                else
                {
                    vitals.QuietTimer += dt;
                    if (vitals.QuietTimer >= vitals.RegenDelay)
                        health.Current = math.min(health.Max, health.Current + vitals.RegenPerSecond * dt);
                }

                if (health.Current <= 0f)
                {
                    health.Current = 0f;
                    vitals.Dead = true;
                    vitals.RespawnTimer = vitals.RespawnSeconds;

                    var possession = em.GetComponentData<PossessionState>(possessionEntity);
                    if (possession.Current == mech)
                    {
                        possession.Current = Entity.Null;
                        possession.Mode = PossessionMode.None;
                        possession.MoveInput = float2.zero;
                        possession.HasAim = false;
                        em.SetComponentData(possessionEntity, possession);
                    }

                    em.SetComponentEnabled<Possessed>(mech, false);
                    em.SetComponentEnabled<ManualAim>(mech, false);
                    em.SetComponentEnabled<Powered>(mech, false);
                    em.AddComponent<DisableRendering>(mech);
                }

                em.SetComponentData(mech, health);
                em.SetComponentData(mech, vitals);
            }

            mechs.Dispose();
        }

        /// <summary>Summed melee damage per second of every agent within reach.</summary>
        static float IncomingDps(
            EntityManager em,
            SpatialGridConfig config,
            SpatialGridData grid,
            float3 position,
            float radius)
        {
            float radiusSq = radius * radius;
            int span = (int)math.ceil(radius / config.CellSize);
            int2 centre = config.ToCell(position);

            float dps = 0f;

            for (int y = centre.y - span; y <= centre.y + span; y++)
            for (int x = centre.x - span; x <= centre.x + span; x++)
            {
                if (!config.InBounds(x, y)) continue;

                int cell = config.Index(x, y);
                for (int i = grid.CellStart[cell]; i < grid.CellStart[cell + 1]; i++)
                {
                    float3 delta = grid.Positions[i] - position;
                    delta.y = 0f;
                    if (math.lengthsq(delta) > radiusSq) continue;

                    var agent = grid.Entities[i];
                    if (!em.HasComponent<AgentCombat>(agent)) continue;

                    var combat = em.GetComponentData<AgentCombat>(agent);
                    dps += combat.Damage * combat.AttackRate;
                }
            }

            return dps;
        }
    }
}
