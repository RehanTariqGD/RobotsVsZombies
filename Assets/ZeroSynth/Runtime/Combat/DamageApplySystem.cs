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
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AgentSteeringSystem))]
    public partial struct DamageApplySystem : ISystem
    {
        EntityQuery m_BufferQuery;

        public void OnCreate(ref SystemState state)
        {
            m_BufferQuery = state.GetEntityQuery(ComponentType.ReadOnly<DamageBuffer>());
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

            // Main thread on purpose: this is a serial reduction over a shared
            // resource, and parallelising it would need per-entity atomics for
            // no benefit at this scale.
            var healthLookup = SystemAPI.GetComponentLookup<Health>();
            var activeLookup = SystemAPI.GetComponentLookup<Active>();

            while (buffer.Pending.TryDequeue(out var evt))
            {
                if (!healthLookup.HasComponent(evt.Target))
                    continue;

                // Already dead or already pooled: drop the hit rather than
                // letting it drive health further negative and re-queue a
                // second death for the same entity.
                if (activeLookup.HasComponent(evt.Target) && !activeLookup.IsComponentEnabled(evt.Target))
                    continue;

                var health = healthLookup[evt.Target];
                if (health.Current <= 0f)
                    continue;

                health.Current -= evt.Amount;
                healthLookup[evt.Target] = health;

                if (health.Current <= 0f)
                    buffer.Deaths.Enqueue(evt.Target);
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

        public void OnCreate(ref SystemState state)
        {
            m_BufferQuery = state.GetEntityQuery(ComponentType.ReadOnly<DamageBuffer>());
            state.RequireForUpdate(m_BufferQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            state.CompleteDependency();

            var buffer = state.EntityManager.GetComponentObject<DamageBuffer>(
                m_BufferQuery.GetSingletonEntity());

            if (buffer.Deaths.Count == 0)
                return;

            var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>();
            var activeLookup = SystemAPI.GetComponentLookup<Active>();

            while (buffer.Deaths.TryDequeue(out var entity))
            {
                if (!activeLookup.HasComponent(entity))
                    continue;

                // Disabling Active drops it out of every gameplay query, and
                // parking it below the map takes it out of the view frustum,
                // so a pooled agent costs one cull test and nothing else.
                activeLookup.SetComponentEnabled(entity, false);

                if (transformLookup.HasComponent(entity))
                {
                    var transform = transformLookup[entity];
                    transform.Position = PoolConstants.ParkPosition;
                    transformLookup[entity] = transform;
                }
            }
        }
    }
}
