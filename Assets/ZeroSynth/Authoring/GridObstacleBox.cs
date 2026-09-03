using Unity.Mathematics;
using UnityEngine;

namespace ZeroSynth.Authoring
{
    /// <summary>
    /// Marks its footprint impassable when the grid is built. Move, scale or
    /// duplicate these in the scene to lay out terrain the swarm must route
    /// around — no painting tool needed yet.
    /// </summary>
    public sealed class GridObstacleBox : MonoBehaviour
    {
        [SerializeField] Vector3 m_Size = new Vector3(10f, 2f, 10f);

        public Bounds WorldBounds => new Bounds(transform.position, m_Size);

        public float3 Min => WorldBounds.min;
        public float3 Max => WorldBounds.max;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.85f, 0.25f, 0.35f, 0.28f);
            Gizmos.DrawCube(transform.position, m_Size);
            Gizmos.color = new Color(0.95f, 0.35f, 0.45f, 0.9f);
            Gizmos.DrawWireCube(transform.position, m_Size);
        }
    }
}
