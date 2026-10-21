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
    /// Allocates the agent pool once at load and activates from it.
    /// </summary>
    /// <remarks>
    /// Every agent the run will ever use is instantiated here. Spawning is
    /// then an <see cref="Active"/> flag flip and a transform write — no
    /// structural change, no allocation, no archetype churn at wave rate.
    /// The pool ceiling is a hard cap: the wave director cannot exceed it, so
    /// size it for the largest wave the design allows.
    /// </remarks>
    public sealed class HordeBootstrap : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField] Mesh m_Mesh;
        [SerializeField] Material m_Material;

        [Tooltip("Off by default: shadow casting submits the whole horde a second time for the shadow pass, doubling its vertex cost.")]
        [SerializeField] bool m_CastShadows;

        [Header("Pool")]
        [Tooltip("Hard ceiling on concurrent agents. Allocated once at load.")]
        [SerializeField, Min(1)] int m_PoolSize = 8192;

        [Header("Waves")]
        [SerializeField] uint m_RunSeed = 20261021;
        [SerializeField, Min(1)] int m_BaseWaveCount = 400;

        [Tooltip("Fractional growth per wave. 0.25 = +25% each wave.")]
        [SerializeField, Min(0f)] float m_CountGrowth = 0.3f;

        [SerializeField, Min(0f)] float m_PrepareDuration = 8f;
        [SerializeField, Min(1f)] float m_SpawnRate = 120f;
        [SerializeField, Min(0f)] float m_SpeedGrowth = 0.04f;
        [SerializeField, Min(0f)] float m_ScrapPerWave = 150f;
        [SerializeField, Min(0f)] float m_BiomassPerKill = 1f;
        [SerializeField, Min(0f)] float m_StartingScrap = 400f;

        [Header("Swarm")]
        [SerializeField, Min(1f)] float m_SpawnInnerRadius = 45f;
        [SerializeField, Min(1f)] float m_SpawnOuterRadius = 85f;
        [SerializeField] float m_MinSpeed = 1.5f;
        [SerializeField] float m_MaxSpeed = 4f;
        [SerializeField] float m_MaxHealth = 30f;

        [Header("Melee")]
        [SerializeField, Min(0f)] float m_AgentDamage = 8f;
        [SerializeField, Min(0.1f)] float m_AgentAttackRate = 1f;

        [Tooltip("How far ahead a wall is attacked from. Roughly one cell.")]
        [SerializeField, Min(0.1f)] float m_AgentReach = 1.2f;

        [Header("Objective")]
        [SerializeField] Transform m_Target;

        [SerializeField] uint m_Seed = 0x5F3759DF;

        EntityManager m_EntityManager;
        NativeArray<Entity> m_Pool;
        Entity m_TargetEntity;
        DamageBuffer m_DamageBuffer;
        Random m_Random;
        bool m_Initialised;

        public int PoolSize => m_Pool.IsCreated ? m_Pool.Length : 0;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Debug.LogError("[ZeroSynth] No default ECS world.", this);
                return;
            }

            if (m_Mesh == null || m_Material == null)
            {
                Debug.LogError("[ZeroSynth] HordeBootstrap needs both a Mesh and a Material assigned.", this);
                return;
            }

            m_EntityManager = world.EntityManager;
            m_Random = Random.CreateFromIndex(m_Seed);

            CreateTargetSingleton();
            CreateDamageBuffer();
            CreateRunSingletons();
            AllocatePool();

            Debug.Log($"[ZeroSynth] Pool {m_Pool.Length} allocated. Wave director will release from it.");

            m_Initialised = true;
        }

        /// <summary>
        /// Spawning lives in WaveDirectorSystem, so the parameters it needs
        /// are published as singletons rather than read off this component.
        /// </summary>
        void CreateRunSingletons()
        {
            var entity = m_EntityManager.CreateEntity();
            m_EntityManager.SetName(entity, "Run");

            m_EntityManager.AddComponentData(entity, new SimClock
            {
                Paused = false,
                Speed = 1f,
                Elapsed = 0f
            });

            m_EntityManager.AddComponentData(entity, new Economy
            {
                Biomass = 0f,
                MetalScrap = m_StartingScrap
            });

            m_EntityManager.AddComponentData(entity, new SpawnProfile
            {
                InnerRadius = m_SpawnInnerRadius,
                OuterRadius = m_SpawnOuterRadius,
                MinSpeed = m_MinSpeed,
                MaxSpeed = m_MaxSpeed,
                MaxHealth = m_MaxHealth,
                BiomassPerKill = m_BiomassPerKill
            });

            m_EntityManager.AddComponentData(entity, new WaveConfig
            {
                Seed = m_RunSeed,
                BaseCount = m_BaseWaveCount,
                CountGrowth = m_CountGrowth,
                PrepareDuration = m_PrepareDuration,
                SpawnRate = m_SpawnRate,
                SpeedGrowth = m_SpeedGrowth,
                ScrapPerWave = m_ScrapPerWave
            });

            // Starts in Preparing with the clock already running, so the first
            // wave arrives after one prep window rather than instantly.
            m_EntityManager.AddComponentData(entity, new WaveState
            {
                Index = 0,
                Phase = WavePhase.Preparing,
                Timer = m_PrepareDuration,
                Remaining = 0,
                SpawnAccumulator = 0f,
                Released = 0
            });
        }

        void Update()
        {
            if (!m_Initialised || m_Target == null) return;

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

        void CreateDamageBuffer()
        {
            m_DamageBuffer = new DamageBuffer();
            m_DamageBuffer.Allocate();

            var entity = m_EntityManager.CreateEntity();
            m_EntityManager.SetName(entity, "DamageBuffer");
            m_EntityManager.AddComponentObject(entity, m_DamageBuffer);
        }

        void AllocatePool()
        {
            var prototype = CreatePrototype();

            m_Pool = new NativeArray<Entity>(m_PoolSize, Allocator.Persistent);
            m_EntityManager.Instantiate(prototype, m_Pool);

            // Everything starts dormant and parked. Activation is the only
            // thing that puts an agent into the world.
            for (int i = 0; i < m_Pool.Length; i++)
            {
                m_EntityManager.SetComponentEnabled<Active>(m_Pool[i], false);
                m_EntityManager.SetComponentData(m_Pool[i],
                    LocalTransform.FromPosition(PoolConstants.ParkPosition));
            }

            m_EntityManager.DestroyEntity(prototype);
        }

        /// <summary>Live agent count. Used by the debug tooling.</summary>
        public int CountActive()
        {
            if (!m_Pool.IsCreated) return 0;

            int n = 0;
            for (int i = 0; i < m_Pool.Length; i++)
                if (m_EntityManager.IsComponentEnabled<Active>(m_Pool[i])) n++;
            return n;
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
            m_EntityManager.AddComponentData(prototype, new Health { Current = m_MaxHealth, Max = m_MaxHealth });
            m_EntityManager.AddComponentData(prototype, new AgentCombat
            {
                Damage = m_AgentDamage,
                AttackRate = m_AgentAttackRate,
                Reach = m_AgentReach,
                Cooldown = 0f
            });
            m_EntityManager.AddComponent<Active>(prototype);
            m_EntityManager.AddComponent<Prefab>(prototype);

            return prototype;
        }

        void OnDestroy()
        {
            if (m_Pool.IsCreated) m_Pool.Dispose();
            m_DamageBuffer?.Dispose();
            m_DamageBuffer = null;
        }
    }
}
