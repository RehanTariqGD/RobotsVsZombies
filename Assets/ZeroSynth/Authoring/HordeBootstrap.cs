using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using Random = Unity.Mathematics.Random;

namespace ZeroSynth.Authoring
{
    /// <summary>
    /// Spawns the swarm at runtime from a single prototype entity.
    /// </summary>
    /// <remarks>
    /// Deliberately not a Baker/SubScene setup. The shipping spawn path needs a
    /// pre-allocated pool that recycles entities rather than creating and
    /// destroying them — structural changes are the dominant cost in ECS — and
    /// building on <c>Instantiate</c> from a prototype is the same shape that
    /// pool will take. Subscenes come back for static level geometry.
    /// </remarks>
    public sealed class HordeBootstrap : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField] Mesh m_Mesh;
        [SerializeField] Material m_Material;

        [Tooltip("Off by default: shadow casting submits the whole horde a second time for the shadow pass, doubling its vertex cost.")]
        [SerializeField] bool m_CastShadows;

        [Header("Swarm")]
        [SerializeField, Min(0)] int m_Count = 2000;
        [SerializeField, Min(1f)] float m_SpawnInnerRadius = 40f;
        [SerializeField, Min(1f)] float m_SpawnOuterRadius = 70f;
        [SerializeField] float m_MinSpeed = 1.5f;
        [SerializeField] float m_MaxSpeed = 3.5f;

        [Header("Objective")]
        [SerializeField] Transform m_Target;

        [SerializeField] uint m_Seed = 0x5F3759DF;

        EntityManager m_EntityManager;
        Entity m_TargetEntity;
        bool m_Initialised;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Debug.LogError("[ZeroSynth] No default ECS world. Is the Entities package initialised?", this);
                return;
            }

            if (m_Mesh == null || m_Material == null)
            {
                Debug.LogError("[ZeroSynth] HordeBootstrap needs both a Mesh and a Material assigned.", this);
                return;
            }

            m_EntityManager = world.EntityManager;

            CreateTargetSingleton();
            SpawnSwarm();

            m_Initialised = true;
        }

        void Update()
        {
            // Keep the objective in sync so the target can be dragged around in
            // play mode. Cheap: one component write per frame, not per agent.
            if (!m_Initialised || m_Target == null)
                return;

            if (m_EntityManager.Exists(m_TargetEntity))
            {
                m_EntityManager.SetComponentData(m_TargetEntity, new HordeTarget
                {
                    Position = m_Target.position
                });
            }
        }

        void CreateTargetSingleton()
        {
            m_TargetEntity = m_EntityManager.CreateEntity(typeof(HordeTarget));
            m_EntityManager.SetName(m_TargetEntity, "HordeTarget");
            m_EntityManager.SetComponentData(m_TargetEntity, new HordeTarget
            {
                Position = m_Target != null ? (float3)m_Target.position : float3.zero
            });
        }

        void SpawnSwarm()
        {
            var prototype = CreatePrototype();

            var entities = new NativeArray<Entity>(m_Count, Allocator.Temp);
            m_EntityManager.Instantiate(prototype, entities);

            var random = Random.CreateFromIndex(m_Seed);

            for (int i = 0; i < entities.Length; i++)
            {
                // Ring spawn: keeps the swarm off the objective at t=0 so the
                // approach is visible rather than starting already arrived.
                float angle = random.NextFloat(0f, math.PI * 2f);
                float radius = random.NextFloat(m_SpawnInnerRadius, m_SpawnOuterRadius);
                float3 position = new float3(math.cos(angle) * radius, 0f, math.sin(angle) * radius);

                m_EntityManager.SetComponentData(entities[i], LocalTransform.FromPosition(position));
                m_EntityManager.SetComponentData(entities[i], new HordeAgent
                {
                    Speed = random.NextFloat(m_MinSpeed, m_MaxSpeed)
                });
            }

            entities.Dispose();
            m_EntityManager.DestroyEntity(prototype);

            Debug.Log($"[ZeroSynth] Spawned {m_Count} horde agents.");
        }

        Entity CreatePrototype()
        {
            var description = new RenderMeshDescription(
                shadowCastingMode: m_CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows: false);

            var renderMeshArray = new RenderMeshArray(
                new[] { m_Material },
                new[] { m_Mesh },
                null);

            var prototype = m_EntityManager.CreateEntity();
            m_EntityManager.SetName(prototype, "HordeAgentPrototype");

            RenderMeshUtility.AddComponents(
                prototype,
                m_EntityManager,
                description,
                renderMeshArray,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0, 0));

            m_EntityManager.AddComponentData(prototype, LocalTransform.FromPosition(float3.zero));
            m_EntityManager.AddComponentData(prototype, new HordeAgent { Speed = 1f });

            // Excludes the prototype itself from every gameplay query while
            // leaving it valid as an Instantiate source.
            m_EntityManager.AddComponent<Prefab>(prototype);

            return prototype;
        }
    }
}
