using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroSynth.Authoring
{
    /// <summary>
    /// The HK gunship: a turret that flies, on a fuel budget.
    /// </summary>
    /// <remarks>
    /// Its guns are ordinary <see cref="Turret"/> components, so targeting,
    /// firing and damage need no special case. It carries no Structure or
    /// Health, which keeps it out of the swarm's target list and out of the
    /// build grid. The object's position is its pad.
    /// </remarks>
    [DefaultExecutionOrder(-50)]
    public sealed class GunshipAuthoring : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField] Mesh m_Mesh;
        [SerializeField] Material m_Material;

        [Header("Flight")]
        [SerializeField, Min(1f)] float m_MoveSpeed = 16f;

        [Tooltip("Radians per second.")]
        [SerializeField, Min(0.5f)] float m_TurnSpeed = 3f;

        [SerializeField, Min(1f)] float m_FlightHeight = 14f;
        [SerializeField, Min(0f)] float m_ParkedHeight = 1.5f;
        [SerializeField, Min(10f)] float m_MaxRange = 70f;

        [Header("Fuel")]
        [SerializeField, Min(1f)] float m_MaxFuel = 20f;
        [SerializeField, Min(0.1f)] float m_BurnPerSecond = 1f;
        [SerializeField, Min(0.1f)] float m_RefillPerSecond = 1.5f;
        [SerializeField, Min(0f)] float m_MinLaunchFuel = 6f;

        [Header("Weapon")]
        [SerializeField, Min(1f)] float m_Range = 24f;
        [SerializeField, Min(0.1f)] float m_FireRate = 10f;
        [SerializeField, Min(1f)] float m_Damage = 12f;
        [SerializeField, Min(0f)] float m_SplashRadius = 2.5f;

        Entity m_Entity;
        EntityManager m_EntityManager;

        public Entity Entity => m_Entity;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            m_EntityManager = world.EntityManager;
            m_Entity = m_EntityManager.CreateEntity();
            m_EntityManager.SetName(m_Entity, "Gunship_" + name);

            float3 pad = transform.position;
            var start = new float3(pad.x, m_ParkedHeight, pad.z);

            m_EntityManager.AddComponentData(m_Entity, LocalTransform.FromPosition(start));
            m_EntityManager.AddComponentData(m_Entity, new GunshipUnit
            {
                MoveSpeed = m_MoveSpeed,
                TurnSpeed = m_TurnSpeed,
                Pad = pad,
                FlightHeight = m_FlightHeight,
                ParkedHeight = m_ParkedHeight,
                MaxRange = m_MaxRange,
                Fuel = m_MaxFuel,
                MaxFuel = m_MaxFuel,
                BurnPerSecond = m_BurnPerSecond,
                RefillPerSecond = m_RefillPerSecond,
                MinLaunchFuel = m_MinLaunchFuel
            });

            m_EntityManager.AddComponentData(m_Entity, new Turret
            {
                Range = m_Range,
                FireRate = m_FireRate,
                Damage = m_Damage,
                SplashRadius = m_SplashRadius,
                Cooldown = 0f
            });
            m_EntityManager.AddComponentData(m_Entity, new TurretStance { Value = TargetingStance.Closest });
            m_EntityManager.AddComponentData(m_Entity, new TurretTarget { Value = Entity.Null });

            // The targeting job requires ManualAim to be present and disabled,
            // and the fire job requires Possessed to be present.
            m_EntityManager.AddComponent<Possessed>(m_Entity);
            m_EntityManager.SetComponentEnabled<Possessed>(m_Entity, false);
            m_EntityManager.AddComponent<ManualAim>(m_Entity);
            m_EntityManager.SetComponentEnabled<ManualAim>(m_Entity, false);

            // Guns are live only while flown; GunshipControlSystem owns this.
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

            // The transform system does not run while the simulation is paused,
            // so the matrix is written here for the first frame.
            if (!m_EntityManager.HasComponent<LocalToWorld>(m_Entity))
                m_EntityManager.AddComponent<LocalToWorld>(m_Entity);

            m_EntityManager.SetComponentData(m_Entity, new LocalToWorld
            {
                Value = float4x4.TRS(
                    m_EntityManager.GetComponentData<LocalTransform>(m_Entity).Position,
                    quaternion.identity,
                    new float3(1f))
            });
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.8f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 2f);
            Gizmos.DrawWireSphere(transform.position, m_MaxRange);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * m_FlightHeight, m_Range);
        }
    }
}
