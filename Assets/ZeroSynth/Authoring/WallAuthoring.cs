using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroSynth.Authoring
{
    /// <summary>
    /// Creates a wall entity spanning a footprint of cells.
    /// </summary>
    /// <remarks>
    /// Snaps to the grid on creation, because <see cref="GridAnchor"/> is the
    /// authoritative position and the transform is derived from it. Manual
    /// placement for now; M4b's build UI produces the same entity shape.
    /// </remarks>
    [DefaultExecutionOrder(-50)]
    public sealed class WallAuthoring : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField] Mesh m_Mesh;
        [SerializeField] Material m_Material;

        [Tooltip("Stretch a unit cube across the footprint. Off for a mesh authored at its real footprint size.")]
        [SerializeField] bool m_StretchToFootprint = true;

        [Header("Footprint")]
        [SerializeField, Min(1)] int m_CellsX = 10;
        [SerializeField, Min(1)] int m_CellsZ = 1;

        [Header("Durability")]
        [SerializeField, Min(1f)] float m_Health = 400f;

        Entity m_Entity;
        EntityManager m_EntityManager;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            m_EntityManager = world.EntityManager;

            var gridAuthoring = FindFirstObjectByType<GridAuthoring>();
            if (gridAuthoring == null)
            {
                Debug.LogError("[ZeroSynth] WallAuthoring needs a GridAuthoring in the scene.", this);
                return;
            }

            var config = gridAuthoring.Config;
            int2 anchor = config.WorldToCell(transform.position);
            var footprint = new int2(m_CellsX, m_CellsZ);

            m_Entity = m_EntityManager.CreateEntity();
            m_EntityManager.SetName(m_Entity, "Wall_" + name);

            m_EntityManager.AddComponentData(m_Entity, new Structure
            {
                Kind = StructureKind.Wall,
                Footprint = footprint
            });
            m_EntityManager.AddComponentData(m_Entity, new GridAnchor { Cell = anchor });
            m_EntityManager.AddComponent<Wall>(m_Entity);
            m_EntityManager.AddComponentData(m_Entity, new Health
            {
                Current = m_Health,
                Max = m_Health
            });

            // Derive the transform from the anchor so the visual lines up with
            // the cells the field actually treats as walled.
            float3 centre = config.CellToWorld(anchor)
                          + new float3((footprint.x - 1) * 0.5f, 0f, (footprint.y - 1) * 0.5f) * config.CellSize;

            m_EntityManager.AddComponentData(m_Entity, new LocalTransform
            {
                Position = centre + new float3(0f, m_StretchToFootprint ? 0.5f : 0f, 0f),
                Rotation = quaternion.identity,
                Scale = 1f
            });

            if (m_Mesh != null && m_Material != null)
                AddRendering(footprint, config.CellSize);

            // Keep the authoring object aligned with where the wall really is.
            transform.position = centre;
        }

        void AddRendering(int2 footprint, float cellSize)
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

            if (!m_StretchToFootprint)
                return;

            // PostTransformMatrix stretches the unit cube across the footprint
            // without touching LocalTransform.Scale, which must stay uniform.
            m_EntityManager.AddComponentData(m_Entity, new PostTransformMatrix
            {
                Value = float4x4.Scale(footprint.x * cellSize, 1.5f, footprint.y * cellSize)
            });
        }

        void OnDrawGizmos()
        {
            var size = new Vector3(m_CellsX, 1.5f, m_CellsZ);
            Gizmos.color = new Color(0.35f, 0.75f, 1f, 0.25f);
            Gizmos.DrawCube(transform.position + Vector3.up * 0.75f, size);
            Gizmos.color = new Color(0.45f, 0.85f, 1f, 0.9f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * 0.75f, size);
        }
    }
}
