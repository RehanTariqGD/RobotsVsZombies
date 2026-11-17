using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroSynth.Authoring
{
    /// <summary>
    /// The objective: a Structure with health whose loss ends the run.
    /// </summary>
    /// <remarks>
    /// Place it where the swarm is headed. The horde's target position is read
    /// from a separate transform, so keep that transform on the same object.
    /// </remarks>
    [DefaultExecutionOrder(-50)]
    public sealed class CoreAuthoring : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField] Mesh m_Mesh;
        [SerializeField] Material m_Material;

        [Header("Durability")]
        [SerializeField, Min(1f)] float m_Health = 3000f;

        Entity m_Entity;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            var em = world.EntityManager;
            m_Entity = em.CreateEntity();
            em.SetName(m_Entity, "Core");

            em.AddComponentData(m_Entity, LocalTransform.FromPosition(transform.position));
            em.AddComponentData(m_Entity, new Structure
            {
                Kind = StructureKind.Core,
                Footprint = new int2(3, 3)
            });
            em.AddComponent<Core>(m_Entity);
            em.AddComponentData(m_Entity, new Health { Current = m_Health, Max = m_Health });

            ReserveFootprint(new int2(3, 3));

            if (m_Mesh == null || m_Material == null)
                return;

            var description = new RenderMeshDescription(
                shadowCastingMode: ShadowCastingMode.Off,
                receiveShadows: false);

            var array = new RenderMeshArray(new[] { m_Material }, new[] { m_Mesh }, null);

            RenderMeshUtility.AddComponents(
                m_Entity,
                em,
                description,
                array,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0, 0));
        }

        /// <summary>
        /// Marks the core's cells as not buildable. They are Reserved rather than
        /// Wall, so pathing is unaffected: agents reach the core through the
        /// flow field and bite it, they do not path around it.
        /// </summary>
        void ReserveFootprint(int2 footprint)
        {
            var grid = FindFirstObjectByType<GridAuthoring>();
            if (grid == null || grid.Grid == null) return;

            var config = grid.Config;
            int2 anchor = config.AnchorForCentre(transform.position, footprint);

            for (int y = 0; y < footprint.y; y++)
            for (int x = 0; x < footprint.x; x++)
            {
                int cx = anchor.x + x;
                int cy = anchor.y + y;
                if (config.InBounds(cx, cy))
                    grid.Grid.Flags[config.Index(cx, cy)] |= CellFlags.Reserved;
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.6f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * 1.5f, new Vector3(3f, 3f, 3f));
        }
    }
}
