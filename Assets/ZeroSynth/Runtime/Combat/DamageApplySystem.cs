using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace ZeroSynth
{
    /// <summary>
    /// Drains the damage queue and applies it. The only writer of
    /// <see cref="Health"/> anywhere in the project.
    /// </summary>
    /// <remarks>
    /// Centralising the write is what makes overkill correct: several sources
    /// hitting the same agent in one frame resolve in a defined order instead
    /// of racing, and an entity cannot be killed twice.
    ///
    /// It is also the one place that sees every hit land, so it is where the
    /// presentation counters (hits, core damage, structures lost) are kept.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AgentSteeringSystem))]
    public partial struct DamageApplySystem : ISystem
    {
        EntityQuery m_BufferQuery;
        EntityQuery m_FeedbackQuery;

        public void OnCreate(ref SystemState state)
        {
            m_BufferQuery = state.GetEntityQuery(ComponentType.ReadOnly<DamageBuffer>());
            m_FeedbackQuery = state.GetEntityQuery(ComponentType.ReadOnly<SimFeedback>());
            state.RequireForUpdate(m_BufferQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            // Producers write to the queue from parallel jobs, so every one of
            // them must have finished before the queue is read here.
            state.CompleteDependency();

            var buffer = state.EntityManager.GetComponentObject<DamageBuffer>(
                m_BufferQuery.GetSingletonEntity());

            if (buffer.Pending.Count == 0)
                return;

            SimFeedback feedback = m_FeedbackQuery.IsEmpty
                ? null
                : state.EntityManager.GetComponentObject<SimFeedback>(m_FeedbackQuery.GetSingletonEntity());

            // Main thread on purpose: this is a serial reduction over a shared
            // resource, and parallelising it would need per-entity atomics for
            // no benefit at this scale.
            var healthLookup = SystemAPI.GetComponentLookup<Health>();
            var activeLookup = SystemAPI.GetComponentLookup<Active>(true);
            var coreLookup = SystemAPI.GetComponentLookup<Core>(true);

            while (buffer.Pending.TryDequeue(out var evt))
            {
                if (!healthLookup.HasComponent(evt.Target))
                    continue;

                bool isAgent = activeLookup.HasComponent(evt.Target);

                // Already dead or already pooled: drop the hit rather than
                // letting it drive health further negative and re-queue a
                // second death for the same entity.
                if (isAgent && !activeLookup.IsComponentEnabled(evt.Target))
                    continue;

                var health = healthLookup[evt.Target];
                if (health.Current <= 0f)
                    continue;

                health.Current -= evt.Amount;
                healthLookup[evt.Target] = health;

                if (feedback != null)
                {
                    feedback.Hits++;
                    if (coreLookup.HasComponent(evt.Target))
                        feedback.CoreDamage += evt.Amount;
                }

                if (health.Current <= 0f)
                {
                    buffer.Deaths.Enqueue(evt.Target);

                    if (feedback != null && !isAgent)
                        feedback.StructuresLost++;
                }
            }
        }
    }

    /// <summary>
    /// Returns dead entities to the pool. Nothing is destroyed.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DamageApplySystem))]
    public partial struct RecycleSystem : ISystem
    {
        EntityQuery m_BufferQuery;
        EntityQuery m_FeedbackQuery;

        public void OnCreate(ref SystemState state)
        {
            m_BufferQuery = state.GetEntityQuery(ComponentType.ReadOnly<DamageBuffer>());
            m_FeedbackQuery = state.GetEntityQuery(ComponentType.ReadOnly<SimFeedback>());
            state.RequireForUpdate(m_BufferQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            state.CompleteDependency();

            var buffer = state.EntityManager.GetComponentObject<DamageBuffer>(
                m_BufferQuery.GetSingletonEntity());

            SimFeedback feedback = m_FeedbackQuery.IsEmpty
                ? null
                : state.EntityManager.GetComponentObject<SimFeedback>(m_FeedbackQuery.GetSingletonEntity());

            // Queues nobody is draining must not grow forever.
            if (feedback != null)
                feedback.TrimIfUndrained();

            if (buffer.Deaths.Count == 0)
                return;

            var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>();
            var activeLookup = SystemAPI.GetComponentLookup<Active>();
            var bountyLookup = SystemAPI.GetComponentLookup<Bounty>(true);
            var heightLookup = SystemAPI.GetComponentLookup<LodHeight>(true);

            float biomass = 0f;

            while (buffer.Deaths.TryDequeue(out var entity))
            {
                if (!activeLookup.HasComponent(entity))
                    continue;

                if (bountyLookup.HasComponent(entity))
                    biomass += bountyLookup[entity].Biomass;

                if (transformLookup.HasComponent(entity))
                {
                    var transform = transformLookup[entity];

                    // Underground agents die out of sight; a burst there would
                    // appear to come from nowhere.
                    if (feedback != null && transform.Position.y > -0.5f)
                    {
                        feedback.Deaths.Enqueue(new DeathEvent
                        {
                            Position = transform.Position,
                            Size = heightLookup.HasComponent(entity) ? heightLookup[entity].Meters : 1.8f
                        });
                    }

                    // Disabling Active drops it out of every gameplay query, and
                    // parking it below the map takes it out of the view frustum,
                    // so a pooled agent costs one cull test and nothing else.
                    transform.Position = PoolConstants.ParkPosition;
                    transformLookup[entity] = transform;
                }

                activeLookup.SetComponentEnabled(entity, false);
            }

            // Accumulated across the whole drain, so the economy takes one
            // write per frame rather than one per corpse.
            if (biomass > 0f && SystemAPI.HasSingleton<Economy>())
                SystemAPI.GetSingletonRW<Economy>().ValueRW.Biomass += biomass;
        }
    }
}
