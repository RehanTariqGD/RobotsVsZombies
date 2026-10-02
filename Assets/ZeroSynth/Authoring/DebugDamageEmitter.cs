using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace ZeroSynth.Authoring
{
    /// <summary>
    /// Stand-in damage source for testing M2 before turrets exist. Brute-force
    /// radius query — turret targeting gets the shared spatial grid in M3.
    /// </summary>
    public sealed class DebugDamageEmitter : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] float m_Radius = 20f;
        [SerializeField, Min(0.1f)] float m_Damage = 15f;

        [Tooltip("Fire automatically on an interval. Zero disables the pulse; use the context menu instead.")]
        [SerializeField, Min(0f)] float m_PulseInterval;

        EntityManager m_EntityManager;
        EntityQuery m_AgentQuery;
        EntityQuery m_BufferQuery;
        float m_NextPulse;
        bool m_Ready;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            m_EntityManager = world.EntityManager;
            m_AgentQuery = m_EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<HordeAgent>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Active>());
            m_BufferQuery = m_EntityManager.CreateEntityQuery(ComponentType.ReadOnly<DamageBuffer>());
            m_Ready = true;
        }

        void Update()
        {
            if (!m_Ready || m_PulseInterval <= 0f) return;

            if (Time.time >= m_NextPulse)
            {
                m_NextPulse = Time.time + m_PulseInterval;
                Fire();
            }
        }

        [ContextMenu("Fire")]
        public void Fire()
        {
            if (!m_Ready || m_BufferQuery.IsEmpty) return;

            var buffer = m_EntityManager.GetComponentObject<DamageBuffer>(
                m_BufferQuery.GetSingletonEntity());

            var entities = m_AgentQuery.ToEntityArray(Allocator.Temp);
            var transforms = m_AgentQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float3 centre = transform.position;
            float radiusSq = m_Radius * m_Radius;
            int hits = 0;

            for (int i = 0; i < entities.Length; i++)
            {
                float3 delta = transforms[i].Position - centre;
                delta.y = 0f;

                if (math.lengthsq(delta) > radiusSq) continue;

                buffer.Pending.Enqueue(new DamageEvent
                {
                    Target = entities[i],
                    Amount = m_Damage
                });
                hits++;
            }

            entities.Dispose();
            transforms.Dispose();

            Debug.Log($"[ZeroSynth] Debug damage: {hits} agent(s) hit for {m_Damage}.");
        }

        [ContextMenu("Log pool state")]
        public void LogPoolState()
        {
            var bootstrap = FindFirstObjectByType<HordeBootstrap>();
            if (bootstrap == null) { Debug.Log("[ZeroSynth] No HordeBootstrap."); return; }
            Debug.Log($"[ZeroSynth] Pool {bootstrap.PoolSize}, active {bootstrap.CountActive()}.");
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, m_Radius);
        }
    }
}
