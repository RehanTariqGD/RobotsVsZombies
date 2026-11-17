using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace ZeroSynth
{
    /// <summary>
    /// Picks a target per turret from the spatial grid, according to stance.
    /// </summary>
    /// <remarks>
    /// Runs after steering so turrets aim at where agents are this frame.
    /// Targeting before movement introduces a one-frame lead error that looks
    /// like turrets shooting at ghosts.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AgentSteeringSystem))]
    public partial struct TurretTargetingSystem : ISystem
    {
        EntityQuery m_DataQuery;

        public void OnCreate(ref SystemState state)
        {
            m_DataQuery = state.GetEntityQuery(ComponentType.ReadOnly<SpatialGridData>());
            state.RequireForUpdate<SpatialGridConfig>();
            state.RequireForUpdate<Turret>();
            state.RequireForUpdate(m_DataQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            var grid = state.EntityManager.GetComponentObject<SpatialGridData>(
                m_DataQuery.GetSingletonEntity());

            state.Dependency = new AcquireTargetJob
            {
                Config = SystemAPI.GetSingleton<SpatialGridConfig>(),
                CellStart = grid.CellStart,
                Entities = grid.Entities,
                Positions = grid.Positions,
                Health = SystemAPI.GetComponentLookup<Health>(true),
                Agents = SystemAPI.GetComponentLookup<HordeAgent>(true)
            }.ScheduleParallel(state.Dependency);

            // Reads the spatial grid's arrays, which another system rewrites
            // every frame. See AgentSteeringSystem for why this completes.
            state.CompleteDependency();
        }
    }

    [BurstCompile]
    [WithAll(typeof(Powered))]
    [WithDisabled(typeof(ManualAim))]
    public partial struct AcquireTargetJob : IJobEntity
    {
        public SpatialGridConfig Config;

        [ReadOnly] public NativeArray<int> CellStart;
        [ReadOnly] public NativeArray<Entity> Entities;
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public ComponentLookup<Health> Health;
        [ReadOnly] public ComponentLookup<HordeAgent> Agents;

        public void Execute(ref TurretTarget target, in Turret turret, in TurretStance stance, in LocalTransform transform)
        {
            float3 origin = transform.Position;
            float rangeSq = turret.Range * turret.Range;

            int span = (int)math.ceil(turret.Range / Config.CellSize);
            int2 centre = Config.ToCell(origin);

            Entity best = Entity.Null;
            float bestScore = float.MinValue;

            for (int y = centre.y - span; y <= centre.y + span; y++)
            for (int x = centre.x - span; x <= centre.x + span; x++)
            {
                if (!Config.InBounds(x, y)) continue;

                int cell = Config.Index(x, y);
                int from = CellStart[cell];
                int to = CellStart[cell + 1];

                for (int i = from; i < to; i++)
                {
                    float3 delta = Positions[i] - origin;
                    delta.y = 0f;

                    float distSq = math.lengthsq(delta);
                    if (distSq > rangeSq) continue;

                    var candidate = Entities[i];
                    float score;

                    switch (stance.Value)
                    {
                        case TargetingStance.Strongest:
                            score = Health.HasComponent(candidate) ? Health[candidate].Current : 0f;
                            break;
                        case TargetingStance.Fastest:
                            score = Agents.HasComponent(candidate) ? Agents[candidate].Speed : 0f;
                            break;
                        default:
                            // Negated distance so "largest score wins" holds
                            // for every stance and the loop stays branch-light.
                            score = -distSq;
                            break;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
            }

            target.Value = best;
        }
    }

    /// <summary>
    /// Applies damage to the current target.
    /// </summary>
    /// <remarks>
    /// Hitscan for now. Projectiles slot in here and nowhere else: targeting,
    /// damage application and recycling are all unaffected by the swap.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TurretTargetingSystem))]
    [UpdateBefore(typeof(DamageApplySystem))]
    public partial struct TurretFireSystem : ISystem
    {
        EntityQuery m_BufferQuery;
        EntityQuery m_GridQuery;
        EntityQuery m_FeedbackQuery;

        public void OnCreate(ref SystemState state)
        {
            m_BufferQuery = state.GetEntityQuery(ComponentType.ReadOnly<DamageBuffer>());
            m_GridQuery = state.GetEntityQuery(ComponentType.ReadOnly<SpatialGridData>());
            m_FeedbackQuery = state.GetEntityQuery(ComponentType.ReadOnly<SimFeedback>());
            state.RequireForUpdate<Turret>();
            state.RequireForUpdate(m_BufferQuery);
            state.RequireForUpdate(m_GridQuery);
            state.RequireForUpdate(m_FeedbackQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            var buffer = state.EntityManager.GetComponentObject<DamageBuffer>(
                m_BufferQuery.GetSingletonEntity());
            var grid = state.EntityManager.GetComponentObject<SpatialGridData>(
                m_GridQuery.GetSingletonEntity());
            var feedback = state.EntityManager.GetComponentObject<SimFeedback>(
                m_FeedbackQuery.GetSingletonEntity());

            state.Dependency = new FireJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                Config = SystemAPI.GetSingleton<SpatialGridConfig>(),
                CellStart = grid.CellStart,
                Entities = grid.Entities,
                Positions = grid.Positions,
                Transforms = SystemAPI.GetComponentLookup<LocalTransform>(true),
                Writer = buffer.Pending.AsParallelWriter(),
                Shots = feedback.Shots.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);

            // Several systems write the damage queue from jobs, and the queue
            // belongs to a managed singleton, so nothing orders them. Complete
            // before the next producer schedules.
            state.CompleteDependency();
        }
    }

    // EnabledRefRO<Possessed> goes into the generated query as a plain All, which
    // for an enableable component means "enabled". Every unpossessed turret has
    // it disabled, so without WithPresent no turret would ever fire.
    [BurstCompile]
    [WithAll(typeof(Powered))]
    [WithPresent(typeof(Possessed))]
    public partial struct FireJob : IJobEntity
    {
        public float DeltaTime;
        public SpatialGridConfig Config;

        [ReadOnly] public NativeArray<int> CellStart;
        [ReadOnly] public NativeArray<Entity> Entities;
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public ComponentLookup<LocalTransform> Transforms;

        public NativeQueue<DamageEvent>.ParallelWriter Writer;
        public NativeQueue<ShotEvent>.ParallelWriter Shots;

        public void Execute(ref Turret turret, in TurretTarget target, in LocalTransform transform, EnabledRefRO<Possessed> possessed)
        {
            float rate = turret.FireRate * (possessed.ValueRO ? PossessionTuning.FireRateBonus : 1f);
            if (rate <= 0f) return;

            // Floored at zero. Letting it run negative while idle banks time:
            // when a target finally appeared, a turret that had waited ten
            // seconds would fire every frame until the debt was paid off.
            turret.Cooldown = math.max(0f, turret.Cooldown - DeltaTime);
            if (turret.Cooldown > 0f) return;

            if (target.Value == Entity.Null || !Transforms.HasComponent(target.Value))
                return;

            turret.Cooldown = 1f / rate;

            Writer.Enqueue(new DamageEvent { Target = target.Value, Amount = turret.Damage });

            // Presentation only: a tracer from muzzle height to chest height.
            Shots.Enqueue(new ShotEvent
            {
                From = transform.Position + new float3(0f, 1f, 0f),
                To = Transforms[target.Value].Position + new float3(0f, 0.9f, 0f),
                Kind = ShotKind.Turret
            });

            if (turret.SplashRadius <= 0f)
                return;

            // Splash reuses the same grid rather than a second query
            // structure, and hits the primary target twice by design — the
            // direct hit plus the blast.
            float3 impact = Transforms[target.Value].Position;
            float splashSq = turret.SplashRadius * turret.SplashRadius;
            int span = (int)math.ceil(turret.SplashRadius / Config.CellSize);
            int2 centre = Config.ToCell(impact);

            for (int y = centre.y - span; y <= centre.y + span; y++)
            for (int x = centre.x - span; x <= centre.x + span; x++)
            {
                if (!Config.InBounds(x, y)) continue;

                int cell = Config.Index(x, y);
                for (int i = CellStart[cell]; i < CellStart[cell + 1]; i++)
                {
                    float3 delta = Positions[i] - impact;
                    delta.y = 0f;
                    if (math.lengthsq(delta) > splashSq) continue;

                    Writer.Enqueue(new DamageEvent
                    {
                        Target = Entities[i],
                        Amount = turret.Damage * 0.5f
                    });
                }
            }
        }
    }
}
