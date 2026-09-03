using Unity.Mathematics;
using UnityEngine;

namespace ZeroSynth.Authoring
{
    /// <summary>
    /// Draws the flow field in the Scene view during play. Sampled on a stride
    /// and clipped to a radius — 65k arrows would be unreadable and would stall
    /// the editor.
    /// </summary>
    [RequireComponent(typeof(GridAuthoring))]
    public sealed class GridDebugDrawer : MonoBehaviour
    {
        public enum Mode { Off, Direction, Integration, Flags }

        [SerializeField] Mode m_Mode = Mode.Direction;

        [Tooltip("Draw every Nth cell. 1 draws all of them.")]
        [SerializeField, Min(1)] int m_Stride = 4;

        [Tooltip("Only draw cells within this radius of the focus point.")]
        [SerializeField, Min(1f)] float m_Radius = 60f;

        [Tooltip("Centre of the drawn region. Leave empty to use the grid centre.")]
        [SerializeField] Transform m_Focus;

        GridAuthoring m_Authoring;

        void Awake() => m_Authoring = GetComponent<GridAuthoring>();

        void OnDrawGizmos()
        {
            if (!Application.isPlaying || m_Mode == Mode.Off) return;
            if (m_Authoring == null) m_Authoring = GetComponent<GridAuthoring>();

            var grid = m_Authoring != null ? m_Authoring.Grid : null;
            if (grid == null || !grid.Direction.IsCreated) return;

            var config = m_Authoring.Config;
            float3 focus = m_Focus != null
                ? (float3)m_Focus.position
                : config.CellToWorld(new int2(config.Width / 2, config.Height / 2));

            int2 focusCell = config.WorldToCell(focus);
            int span = Mathf.CeilToInt(m_Radius / config.CellSize);
            float half = config.CellSize * 0.5f;

            for (int y = focusCell.y - span; y <= focusCell.y + span; y += m_Stride)
            for (int x = focusCell.x - span; x <= focusCell.x + span; x += m_Stride)
            {
                if (!config.InBounds(x, y)) continue;

                int i = config.Index(x, y);
                float3 centre = config.CellToWorld(new int2(x, y));

                switch (m_Mode)
                {
                    case Mode.Direction:
                    {
                        if ((grid.Flags[i] & CellFlags.Impassable) != 0)
                        {
                            Gizmos.color = new Color(0.85f, 0.25f, 0.35f, 0.7f);
                            Gizmos.DrawWireCube(centre, new Vector3(config.CellSize, 0.1f, config.CellSize));
                            break;
                        }

                        float2 d = grid.Direction[i];
                        if (math.lengthsq(d) < 1e-6f) continue;

                        float3 dir = new float3(d.x, 0f, d.y) * half * m_Stride * 0.8f;
                        Gizmos.color = new Color(0.35f, 0.72f, 1f, 0.85f);
                        Gizmos.DrawRay(centre, dir);
                        Gizmos.DrawSphere(centre + dir, config.CellSize * 0.08f);
                        break;
                    }

                    case Mode.Integration:
                    {
                        float v = grid.Integration[i];
                        if (v == float.MaxValue)
                        {
                            Gizmos.color = new Color(0.5f, 0.1f, 0.1f, 0.5f);
                        }
                        else
                        {
                            float t = math.saturate(v / (m_Radius * 2f));
                            Gizmos.color = new Color(t, 1f - t, 0.25f, 0.55f);
                        }
                        Gizmos.DrawCube(centre, new Vector3(config.CellSize * 0.8f, 0.05f, config.CellSize * 0.8f));
                        break;
                    }

                    case Mode.Flags:
                    {
                        var f = grid.Flags[i];
                        if (f == CellFlags.None) continue;
                        Gizmos.color = (f & CellFlags.Impassable) != 0
                            ? new Color(0.85f, 0.25f, 0.35f, 0.6f)
                            : new Color(0.95f, 0.75f, 0.2f, 0.6f);
                        Gizmos.DrawCube(centre, new Vector3(config.CellSize * 0.9f, 0.05f, config.CellSize * 0.9f));
                        break;
                    }
                }
            }
        }
    }
}
