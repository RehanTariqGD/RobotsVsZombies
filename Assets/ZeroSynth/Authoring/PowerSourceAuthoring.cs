using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroSynth.Authoring
{
    /// <summary>
    /// A reactor or relay pylon. Reactors originate power; pylons extend it.
    /// </summary>
    /// <remarks>
    /// Both are the same entity shape with one flag different, which is what
    /// lets the flood use a single rule for the whole network.
    /// </remarks>
    [DefaultExecutionOrder(-50)]
    public sealed class PowerSourceAuthoring : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField] Mesh m_Mesh;
        [SerializeField] Material m_Material;

        [Header("Power")]
        [Tooltip("Reactors originate power. Pylons only relay it.")]
        [SerializeField] bool m_IsReactor = true;

        [Tooltip("How far power carries from here.")]
        [SerializeField, Min(1f)] float m_SupplyRange = 35f;

        [Header("Durability")]
        [SerializeField, Min(1f)] float m_Health = 500f;

        Entity m_Entity;
        EntityManager m_EntityManager;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            m_EntityManager = world.EntityManager;
            m_Entity = m_EntityManager.CreateEntity();
            m_EntityManager.SetName(m_Entity, (m_IsReactor ? "Reactor_" : "Pylon_") + name);

            m_EntityManager.AddComponentData(m_Entity,
                LocalTransform.FromPosition(transform.position));

            m_EntityManager.AddComponentData(m_Entity, new Structure
            {
                Kind = m_IsReactor ? StructureKind.Reactor : StructureKind.Pylon,
                Footprint = new int2(2, 2)
            });

            m_EntityManager.AddComponentData(m_Entity, new PowerNode
            {
                SupplyRange = m_SupplyRange,
                IsSource = m_IsReactor
            });
            m_EntityManager.AddComponent<Powered>(m_Entity);
            m_EntityManager.SetComponentEnabled<Powered>(m_Entity, false);

            m_EntityManager.AddComponentData(m_Entity, new Health
            {
                Current = m_Health,
                Max = m_Health
            });

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
                Value = float4x4.Scale(2f, 3f, 2f)
            });
        }

        /// <summary>Kills this node, to watch the outage cascade.</summary>
        [ContextMenu("Destroy (test outage)")]
        void KillNode()
        {
            if (m_EntityManager == default || !m_EntityManager.Exists(m_Entity)) return;
            m_EntityManager.SetComponentData(m_Entity, new Health { Current = 0f, Max = m_Health });
            Debug.Log($"[ZeroSynth] {name} destroyed — downstream nodes should go dark.");
        }

        void OnDrawGizmos()
        {
            Gizmos.color = m_IsReactor
                ? new Color(1f, 0.85f, 0.2f, 0.5f)
                : new Color(0.4f, 0.8f, 1f, 0.45f);
            Gizmos.DrawWireSphere(transform.position, m_SupplyRange);
        }
    }
}
