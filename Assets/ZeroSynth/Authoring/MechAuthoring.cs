using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroSynth.Authoring
{
    /// <summary>
    /// A Goliath mech: a turret that walks.
    /// </summary>
    /// <remarks>
    /// Weapons come from the ordinary <see cref="Turret"/> components, so
    /// nothing in the targeting, firing or damage path knows a mech from a
    /// pillbox. It carries <see cref="Powered"/> enabled but no
    /// <see cref="PowerNode"/>, which keeps it out of the power flood entirely
    /// — a mobile unit does not depend on the grid.
    /// </remarks>
    [DefaultExecutionOrder(-50)]
    public sealed class MechAuthoring : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField] Mesh m_Mesh;
        [SerializeField] Material m_Material;

        [Header("Chassis")]
        [SerializeField, Min(1f)] float m_MoveSpeed = 7f;

        [Tooltip("Radians per second. Low values read as heavy.")]
        [SerializeField, Min(0.5f)] float m_TurnSpeed = 4f;

        [SerializeField, Min(1f)] float m_Health = 1200f;

        [Header("Weapon")]
        [SerializeField, Min(1f)] float m_Range = 20f;
        [SerializeField, Min(0.1f)] float m_FireRate = 8f;
        [SerializeField, Min(1f)] float m_Damage = 14f;
        [SerializeField, Min(0f)] float m_SplashRadius = 3f;

        Entity m_Entity;
        EntityManager m_EntityManager;

        public Entity Entity => m_Entity;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            m_EntityManager = world.EntityManager;
            m_Entity = m_EntityManager.CreateEntity();
            m_EntityManager.SetName(m_Entity, "Mech_" + name);

            m_EntityManager.AddComponentData(m_Entity,
                LocalTransform.FromPosition(transform.position));

            m_EntityManager.AddComponentData(m_Entity, new MechUnit
            {
                MoveSpeed = m_MoveSpeed,
                TurnSpeed = m_TurnSpeed
            });

            m_EntityManager.AddComponentData(m_Entity, new Structure
            {
                Kind = StructureKind.Turret,
                Footprint = new int2(2, 2)
            });

            m_EntityManager.AddComponentData(m_Entity, new Turret
            {
                Range = m_Range,
                FireRate = m_FireRate,
                Damage = m_Damage,
                SplashRadius = m_SplashRadius,
                Cooldown = 0f
            });
            m_EntityManager.AddComponentData(m_Entity, new TurretStance
            {
                Value = TargetingStance.Closest
            });
            m_EntityManager.AddComponentData(m_Entity, new TurretTarget
            {
                Value = Entity.Null
            });

            m_EntityManager.AddComponentData(m_Entity, new Health
            {
                Current = m_Health,
                Max = m_Health
            });

            m_EntityManager.AddComponent<Possessed>(m_Entity);
            m_EntityManager.SetComponentEnabled<Possessed>(m_Entity, false);

            // Driven, not hand-aimed: the weapon keeps auto-targeting while
            // the player steers.
            m_EntityManager.AddComponent<ManualAim>(m_Entity);
            m_EntityManager.SetComponentEnabled<ManualAim>(m_Entity, false);

            // No PowerNode, so the flood never sees it and this stays on.
            m_EntityManager.AddComponent<Powered>(m_Entity);
            m_EntityManager.SetComponentEnabled<Powered>(m_Entity, true);

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

            m_EntityManager.AddComponentData(m_Entity, new PostTransformMatrix
            {
                Value = float4x4.Scale(2.5f, 4f, 2.5f)
            });
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, m_Range);
        }
    }
}
