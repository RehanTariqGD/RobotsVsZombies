using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroSynth.Authoring
{
    /// <summary>
    /// Creates a turret entity at this transform's position.
    /// </summary>
    /// <remarks>
    /// Placement is manual for now; M4 replaces this with grid placement
    /// driven by the build UI. The entity shape it produces is the shape
    /// placement will produce, so nothing downstream changes.
    /// </remarks>
    [DefaultExecutionOrder(-50)]
    public sealed class TurretAuthoring : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField] Mesh m_Mesh;
        [SerializeField] Material m_Material;

        [Header("Weapon")]
        [SerializeField, Min(1f)] float m_Range = 25f;

        [Tooltip("Shots per second.")]
        [SerializeField, Min(0.1f)] float m_FireRate = 4f;

        [SerializeField, Min(1f)] float m_Damage = 10f;

        [Tooltip("Zero means single target. Splash hits for half damage.")]
        [SerializeField, Min(0f)] float m_SplashRadius;

        [SerializeField] TargetingStance m_Stance = TargetingStance.Closest;

        [Header("Durability")]
        [SerializeField, Min(1f)] float m_Health = 400f;

        Entity m_Entity;
        EntityManager m_EntityManager;

        public TargetingStance Stance => m_Stance;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            m_EntityManager = world.EntityManager;
            m_Entity = m_EntityManager.CreateEntity();
            m_EntityManager.SetName(m_Entity, "Turret_" + name);

            m_EntityManager.AddComponentData(m_Entity,
                LocalTransform.FromPosition(transform.position));

            m_EntityManager.AddComponentData(m_Entity, new Turret
            {
                Range = m_Range,
                FireRate = m_FireRate,
                Damage = m_Damage,
                SplashRadius = m_SplashRadius,
                Cooldown = 0f
            });
            // Structure + Health make it a valid target for spitters. A dead
            // turret is silenced by the power flood, which stops powering any
            // node whose health has reached zero.
            m_EntityManager.AddComponentData(m_Entity, new Structure
            {
                Kind = StructureKind.Turret,
                Footprint = new Unity.Mathematics.int2(1, 1)
            });
            m_EntityManager.AddComponentData(m_Entity, new Health
            {
                Current = m_Health,
                Max = m_Health
            });
            m_EntityManager.AddComponentData(m_Entity, new TurretStance { Value = m_Stance });
            m_EntityManager.AddComponentData(m_Entity, new TurretTarget { Value = Entity.Null });

            // Present but disabled: the fire job reads it every frame, and M6
            // only has to flip it rather than add a component mid-wave.
            m_EntityManager.AddComponent<Possessed>(m_Entity);
            m_EntityManager.SetComponentEnabled<Possessed>(m_Entity, false);

            // Must be present even when unused: auto-targeting queries for
            // WithDisabled(ManualAim), and an entity lacking the component
            // does not match that at all — the turret would never fire.
            m_EntityManager.AddComponent<ManualAim>(m_Entity);
            m_EntityManager.SetComponentEnabled<ManualAim>(m_Entity, false);

            // Consumer, not a relay: the network cannot route power through a
            // turret. Starts unpowered until the flood reaches it.
            m_EntityManager.AddComponentData(m_Entity, new PowerNode
            {
                SupplyRange = 0f,
                IsSource = false
            });
            m_EntityManager.AddComponent<Powered>(m_Entity);
            m_EntityManager.SetComponentEnabled<Powered>(m_Entity, false);

            if (m_Mesh != null && m_Material != null)
                AddRendering();
        }

        void AddRendering()
        {
            var description = new RenderMeshDescription(
                shadowCastingMode: ShadowCastingMode.Off,
                receiveShadows: false);

            var array = new RenderMeshArray(new[] { m_Material }, new[] { m_Mesh }, null);

            RenderMeshUtility.AddComponents(
                m_Entity,
                m_EntityManager,
                description,
                array,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0, 0));
        }

        /// <summary>Runtime stance switch, for testing the three modes.</summary>
        public void SetStance(TargetingStance stance)
        {
            m_Stance = stance;
            if (m_EntityManager != default && m_EntityManager.Exists(m_Entity))
                m_EntityManager.SetComponentData(m_Entity, new TurretStance { Value = stance });
        }

        [ContextMenu("Cycle stance")]
        void CycleStance()
        {
            SetStance((TargetingStance)(((int)m_Stance + 1) % 3));
            Debug.Log($"[ZeroSynth] {name} stance -> {m_Stance}");
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.85f);
            Gizmos.DrawWireSphere(transform.position, m_Range);

            if (m_SplashRadius > 0f)
            {
                Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.7f);
                Gizmos.DrawWireSphere(transform.position, m_SplashRadius);
            }
        }
    }
}
