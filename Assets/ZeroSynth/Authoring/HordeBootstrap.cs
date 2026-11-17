using System;
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
    /// One kind of agent: what it looks like, how big its pool is, and how it
    /// fights.
    /// </summary>
    [Serializable]
    public sealed class UnitDefinition
    {
        public string Name;
        public UnitKind Kind;
        public Mesh Mesh;
        public Material Material;

        [Tooltip("Allocated once at load. Hard ceiling on concurrent agents of this kind.")]
        [Min(0)] public int PoolSize = 1000;

        [Header("Stats")]
        [Min(1f)] public float Health = 30f;
        public float MinSpeed = 1.5f;
        public float MaxSpeed = 3.5f;

        [Tooltip("Biomass paid out on death.")]
        [Min(0f)] public float Bounty = 1f;

        [Header("Melee (walls). Zero damage means no melee.")]
        [Min(0f)] public float MeleeDamage = 8f;
        [Min(0.1f)] public float MeleeRate = 1f;
        [Min(0.1f)] public float MeleeReach = 1.2f;

        [Header("Ranged (structures). Zero damage means no ranged attack.")]
        [Min(0f)] public float RangedDamage;
        [Min(0.1f)] public float RangedRate = 0.7f;
        [Min(1f)] public float RangedRange = 14f;
    }

    /// <summary>
    /// Allocates one agent pool per unit kind at load, and publishes the run
    /// singletons the wave director works from.
    /// </summary>
    /// <remarks>
    /// Every agent the run will ever use is instantiated here. Spawning is then
    /// an <see cref="Active"/> flag flip and a transform write — no structural
    /// change, no allocation, no archetype churn at wave rate. Each pool size
    /// is a hard ceiling, so size it for the largest wave the design allows.
    /// Total pool size must not exceed the spatial grid's capacity.
    /// </remarks>
    public sealed class HordeBootstrap : MonoBehaviour
    {
        [Header("Rendering")]
        [Tooltip("Off by default: shadow casting submits the whole horde a second time for the shadow pass, doubling its vertex cost.")]
        [SerializeField] bool m_CastShadows;

        [Header("Units")]
        [SerializeField] UnitDefinition[] m_Units = DefaultUnits();

        [Header("Waves")]
        [SerializeField] uint m_RunSeed = 20261021;
        [SerializeField, Min(1)] int m_BaseWaveCount = 300;
        [SerializeField, Min(0)] int m_SpitterBase = 30;
        [SerializeField, Min(1)] int m_SpitterFirstWave = 2;
        [SerializeField, Min(0)] int m_BurrowerBase = 20;
        [SerializeField, Min(1)] int m_BurrowerFirstWave = 3;

        [Tooltip("A Titan arrives every Nth wave. Zero disables bosses.")]
        [SerializeField, Min(0)] int m_TitanEvery = 5;

        [Tooltip("Fractional growth per wave. 0.25 = +25% each wave.")]
        [SerializeField, Min(0f)] float m_CountGrowth = 0.3f;

        [SerializeField, Min(0f)] float m_PrepareDuration = 8f;
        [SerializeField, Min(1f)] float m_SpawnRate = 120f;
        [SerializeField, Min(0f)] float m_SpeedGrowth = 0.04f;
        [SerializeField, Min(0f)] float m_ScrapPerWave = 150f;
        [SerializeField, Min(0f)] float m_StartingScrap = 400f;

        [Header("Spawn ring")]
        [SerializeField, Min(1f)] float m_SpawnInnerRadius = 45f;
        [SerializeField, Min(1f)] float m_SpawnOuterRadius = 85f;

        [Header("Tunnelling")]
        [Tooltip("Below the ground plane, so tunnelling agents are hidden.")]
        [SerializeField] float m_BurrowDepth = -2.2f;

        [Tooltip("Distance from the objective at which burrowers surface.")]
        [SerializeField, Min(1f)] float m_BurrowSurfaceRadius = 10f;

        [SerializeField, Min(0.1f)] float m_BurrowSpeedMultiplier = 1.3f;

        [Header("Objective")]
        [SerializeField] Transform m_Target;

        EntityManager m_EntityManager;
        NativeList<Entity> m_Pool;
        Entity m_TargetEntity;
        DamageBuffer m_DamageBuffer;
        bool m_Initialised;

        public int PoolSize => m_Pool.IsCreated ? m_Pool.Length : 0;

        /// <summary>
        /// Defaults for a new component. Meshes and materials are assigned in
        /// the scene; the numbers are the starting balance.
        /// </summary>
        static UnitDefinition[] DefaultUnits()
        {
            return new[]
            {
                new UnitDefinition
                {
                    Name = "Runner", Kind = UnitKind.Runner, PoolSize = 5200,
                    Health = 30f, MinSpeed = 1.5f, MaxSpeed = 4f, Bounty = 1f,
                    MeleeDamage = 8f, MeleeRate = 1f, MeleeReach = 1.2f,
                },
                new UnitDefinition
                {
                    Name = "Spitter", Kind = UnitKind.Spitter, PoolSize = 1500,
                    Health = 45f, MinSpeed = 1.2f, MaxSpeed = 2.2f, Bounty = 2f,
                    MeleeDamage = 0f,
                    RangedDamage = 14f, RangedRate = 0.7f, RangedRange = 14f,
                },
                new UnitDefinition
                {
                    Name = "Burrower", Kind = UnitKind.Burrower, PoolSize = 1000,
                    Health = 70f, MinSpeed = 2f, MaxSpeed = 3f, Bounty = 3f,
                    MeleeDamage = 14f, MeleeRate = 1f, MeleeReach = 1.4f,
                },
                new UnitDefinition
                {
                    Name = "Meat Titan", Kind = UnitKind.Titan, PoolSize = 24,
                    Health = 1800f, MinSpeed = 0.9f, MaxSpeed = 1.3f, Bounty = 40f,
                    MeleeDamage = 70f, MeleeRate = 0.7f, MeleeReach = 3f,
                },
            };
        }

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Debug.LogError("[ZeroSynth] No default ECS world.", this);
                return;
            }

            if (!ValidateUnits())
                return;

            m_EntityManager = world.EntityManager;

            CreateTargetSingleton();
            CreateDamageBuffer();
            CreateRunSingletons();
            AllocatePools();

            Debug.Log($"[ZeroSynth] Pools allocated, {m_Pool.Length} agents total. Wave director will release from them.");

            m_Initialised = true;
        }

        bool ValidateUnits()
        {
            if (m_Units == null || m_Units.Length == 0)
            {
                Debug.LogError("[ZeroSynth] HordeBootstrap has no unit definitions.", this);
                return false;
            }

            foreach (var unit in m_Units)
            {
                if (unit.PoolSize > 0 && (unit.Mesh == null || unit.Material == null))
                {
                    Debug.LogError($"[ZeroSynth] Unit '{unit.Name}' needs both a Mesh and a Material.", this);
                    return false;
                }
            }

            return true;
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

        UnitStats StatsFor(UnitKind kind)
        {
            foreach (var unit in m_Units)
            {
                if (unit.Kind != kind) continue;
                return new UnitStats
                {
                    Health = unit.Health,
                    MinSpeed = unit.MinSpeed,
                    MaxSpeed = unit.MaxSpeed
                };
            }

            return default;
        }

        /// <summary>
        /// Spawning lives in WaveDirectorSystem, so the parameters it needs are
        /// published as singletons rather than read off this component.
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
                BurrowDepth = m_BurrowDepth,
                BurrowSurfaceRadius = m_BurrowSurfaceRadius,
                BurrowSpeedMultiplier = m_BurrowSpeedMultiplier
            });

            m_EntityManager.AddComponentData(entity, new UnitTable
            {
                Runner = StatsFor(UnitKind.Runner),
                Spitter = StatsFor(UnitKind.Spitter),
                Burrower = StatsFor(UnitKind.Burrower),
                Titan = StatsFor(UnitKind.Titan)
            });

            m_EntityManager.AddComponentData(entity, new WaveConfig
            {
                Seed = m_RunSeed,
                BaseCount = m_BaseWaveCount,
                SpitterBase = m_SpitterBase,
                SpitterFirstWave = m_SpitterFirstWave,
                BurrowerBase = m_BurrowerBase,
                BurrowerFirstWave = m_BurrowerFirstWave,
                TitanEvery = m_TitanEvery,
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
                Remaining = int4.zero,
                SpawnAccumulator = 0f,
                Released = 0
            });
        }

        void AllocatePools()
        {
            int total = 0;
            foreach (var unit in m_Units) total += unit.PoolSize;

            m_Pool = new NativeList<Entity>(total, Allocator.Persistent);

            foreach (var unit in m_Units)
            {
                if (unit.PoolSize <= 0) continue;

                var prototype = CreatePrototype(unit);
                var batch = new NativeArray<Entity>(unit.PoolSize, Allocator.Temp);
                m_EntityManager.Instantiate(prototype, batch);

                // Everything starts dormant and parked. Activation is the only
                // thing that puts an agent into the world.
                for (int i = 0; i < batch.Length; i++)
                {
                    m_EntityManager.SetComponentEnabled<Active>(batch[i], false);
                    m_EntityManager.SetComponentData(batch[i],
                        LocalTransform.FromPosition(PoolConstants.ParkPosition));
                }

                m_Pool.AddRange(batch);
                batch.Dispose();
                m_EntityManager.DestroyEntity(prototype);
            }
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

        Entity CreatePrototype(UnitDefinition unit)
        {
            var description = new RenderMeshDescription(
                shadowCastingMode: m_CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows: false);

            var renderMeshArray = new RenderMeshArray(
                new[] { unit.Material },
                new[] { unit.Mesh },
                null);

            var prototype = m_EntityManager.CreateEntity();
            m_EntityManager.SetName(prototype, unit.Name + "Prototype");

            RenderMeshUtility.AddComponents(
                prototype,
                m_EntityManager,
                description,
                renderMeshArray,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0, 0));

            m_EntityManager.AddComponentData(prototype, LocalTransform.FromPosition(float3.zero));
            m_EntityManager.AddComponentData(prototype, new HordeAgent { Speed = 1f });
            m_EntityManager.AddComponentData(prototype, new Health { Current = unit.Health, Max = unit.Health });
            m_EntityManager.AddComponentData(prototype, new Bounty { Biomass = unit.Bounty });
            m_EntityManager.AddSharedComponent(prototype, new AgentKind { Value = unit.Kind });

            if (unit.MeleeDamage > 0f)
            {
                m_EntityManager.AddComponentData(prototype, new AgentCombat
                {
                    Damage = unit.MeleeDamage,
                    AttackRate = unit.MeleeRate,
                    Reach = unit.MeleeReach,
                    Cooldown = 0f
                });
            }

            if (unit.RangedDamage > 0f)
            {
                m_EntityManager.AddComponentData(prototype, new RangedAttacker
                {
                    Range = unit.RangedRange,
                    Damage = unit.RangedDamage,
                    AttackRate = unit.RangedRate,
                    Cooldown = 0f,
                    Target = Entity.Null,
                    RetargetTimer = 0f
                });
                m_EntityManager.AddComponent<Holding>(prototype);
                m_EntityManager.SetComponentEnabled<Holding>(prototype, false);
            }

            // Only burrowers carry Underground, so every query that excludes it
            // with WithNone leaves other kinds untouched.
            if (unit.Kind == UnitKind.Burrower)
            {
                m_EntityManager.AddComponent<Underground>(prototype);
                m_EntityManager.SetComponentEnabled<Underground>(prototype, false);
            }

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
