using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace ZeroSynth
{
    /// <summary>
    /// Spitters stop at range and spit acid at the nearest structure.
    /// </summary>
    /// <remarks>
    /// Acid is applied instantly through the damage queue, like turret fire.
    /// There is no projectile entity; a visual can be layered on later without
    /// touching the damage path.
    ///
    /// Structures are not in the spatial grid (that holds agents), so they are
    /// gathered into a flat array each frame. Hundreds of structures against
    /// a staggered retarget is far cheaper than maintaining a second grid.
    /// The mech is excluded: it moves, and it must stay untargetable for the
    /// possession path to be safe.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AgentSteeringSystem))]
    [UpdateBefore(typeof(DamageApplySystem))]
    public partial struct SpitterSystem : ISystem
    {
        EntityQuery m_StructureQuery;
        EntityQuery m_BufferQuery;

        public void OnCreate(ref SystemState state)
        {
            m_StructureQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Structure, Health, LocalTransform>()
                .WithNone<MechUnit>()
                .Build(ref state);

            m_BufferQuery = state.GetEntityQuery(ComponentType.ReadOnly<DamageBuffer>());

            state.RequireForUpdate<RangedAttacker>();
            state.RequireForUpdate(m_BufferQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            var buffer = state.EntityManager.GetComponentObject<DamageBuffer>(
                m_BufferQuery.GetSingletonEntity());

            var entities = m_StructureQuery.ToEntityArray(Allocator.TempJob);
            var transforms = m_StructureQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            var positions = new NativeArray<float3>(transforms.Length, Allocator.TempJob);
            for (int i = 0; i < transforms.Length; i++)
                positions[i] = transforms[i].Position;
            transforms.Dispose();

            var job = new SpitterJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                Structures = entities,
                StructurePositions = positions,
                Healths = SystemAPI.GetComponentLookup<Health>(true),
                Transforms = SystemAPI.GetComponentLookup<LocalTransform>(true),
                Writer = buffer.Pending.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);

            state.Dependency = JobHandle.CombineDependencies(
                entities.Dispose(job),
                positions.Dispose(job));

            // Writes the shared damage queue; see TurretFireSystem.
            state.CompleteDependency();
        }
    }

    [BurstCompile]
    [WithAll(typeof(Active))]
    [WithNone(typeof(Underground))]
    public partial struct SpitterJob : IJobEntity
    {
        public float DeltaTime;

        [ReadOnly] public NativeArray<Entity> Structures;
        [ReadOnly] public NativeArray<float3> StructurePositions;
        [ReadOnly] public ComponentLookup<Health> Healths;
        [ReadOnly] public ComponentLookup<LocalTransform> Transforms;

        public NativeQueue<DamageEvent>.ParallelWriter Writer;

        const float RetargetInterval = 0.4f;

        public void Execute(ref RangedAttacker ranged, EnabledRefRW<Holding> holding, in LocalTransform transform)
        {
            ranged.RetargetTimer -= DeltaTime;

            float3 origin = transform.Position;
            float rangeSq = ranged.Range * ranged.Range;

            bool targetValid = ranged.Target != Entity.Null
                               && Healths.HasComponent(ranged.Target)
                               && Healths[ranged.Target].Current > 0f;

            if (!targetValid || ranged.RetargetTimer <= 0f)
            {
                ranged.Target = FindNearestInRange(origin, rangeSq);
                ranged.RetargetTimer = RetargetInterval;
            }

            if (ranged.Target == Entity.Null || !Transforms.HasComponent(ranged.Target))
            {
                ranged.Target = Entity.Null;
                holding.ValueRW = false;
                return;
            }

            float3 delta = Transforms[ranged.Target].Position - origin;
            delta.y = 0f;

            // Slack on the range so a target at the edge does not flicker
            // between holding and advancing every retarget.
            if (math.lengthsq(delta) > rangeSq * 1.44f)
            {
                ranged.Target = Entity.Null;
                holding.ValueRW = false;
                return;
            }

            holding.ValueRW = true;

            ranged.Cooldown -= DeltaTime;
            if (ranged.Cooldown > 0f || ranged.AttackRate <= 0f)
                return;

            ranged.Cooldown += 1f / ranged.AttackRate;
            Writer.Enqueue(new DamageEvent { Target = ranged.Target, Amount = ranged.Damage });
        }

        Entity FindNearestInRange(float3 origin, float rangeSq)
        {
            Entity best = Entity.Null;
            float bestSq = rangeSq;

            for (int i = 0; i < Structures.Length; i++)
            {
                var candidate = Structures[i];
                if (!Healths.HasComponent(candidate) || Healths[candidate].Current <= 0f)
                    continue;

                float3 delta = StructurePositions[i] - origin;
                delta.y = 0f;

                float d = math.lengthsq(delta);
                if (d <= bestSq)
                {
                    bestSq = d;
                    best = candidate;
                }
            }

            return best;
        }
    }
}
