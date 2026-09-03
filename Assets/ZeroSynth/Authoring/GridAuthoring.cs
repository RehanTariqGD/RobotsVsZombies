using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace ZeroSynth.Authoring
{
    /// <summary>
    /// Creates the grid singletons at runtime and stamps obstacles into them.
    /// </summary>
    /// <remarks>
    /// Runs before <see cref="HordeBootstrap"/> via script execution order so
    /// the field exists on the swarm's first tick.
    /// </remarks>
    [DefaultExecutionOrder(-100)]
    public sealed class GridAuthoring : MonoBehaviour
    {
        [Header("Grid")]
        [SerializeField, Min(2)] int m_Width = 256;
        [SerializeField, Min(2)] int m_Height = 256;
        [SerializeField, Min(0.1f)] float m_CellSize = 1f;

        [Tooltip("Centre the grid on this transform. Leave empty to centre on world origin.")]
        [SerializeField] Transform m_Centre;

        EntityManager m_EntityManager;
        GridData m_Grid;
        Entity m_Entity;

        public GridConfig Config { get; private set; }
        public GridData Grid => m_Grid;

        void Awake()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Debug.LogError("[ZeroSynth] No default ECS world.", this);
                return;
            }

            m_EntityManager = world.EntityManager;

            float3 centre = m_Centre != null ? (float3)m_Centre.position : float3.zero;
            var config = new GridConfig
            {
                CellSize = m_CellSize,
                Width = m_Width,
                Height = m_Height,
                Origin = centre.xz - new float2(m_Width, m_Height) * m_CellSize * 0.5f
            };
            Config = config;

            m_Grid = new GridData();
            m_Grid.Allocate(config.CellCount);

            StampObstacles(config);

            m_Entity = m_EntityManager.CreateEntity();
            m_EntityManager.SetName(m_Entity, "Grid");
            m_EntityManager.AddComponentData(m_Entity, config);
            m_EntityManager.AddComponentObject(m_Entity, m_Grid);

            Debug.Log($"[ZeroSynth] Grid {m_Width}x{m_Height} @ {m_CellSize}m created.");
        }

        void StampObstacles(GridConfig config)
        {
            var boxes = FindObjectsByType<GridObstacleBox>(FindObjectsSortMode.None);
            int stamped = 0;

            foreach (var box in boxes)
            {
                var bounds = box.WorldBounds;

                int2 min = config.WorldToCell(bounds.min);
                int2 max = config.WorldToCell(bounds.max);

                for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    if (!config.InBounds(x, y)) continue;
                    m_Grid.Flags[config.Index(x, y)] |= CellFlags.Impassable;
                    stamped++;
                }
            }

            m_Grid.Dirty = true;

            if (boxes.Length > 0)
                Debug.Log($"[ZeroSynth] Stamped {stamped} impassable cells from {boxes.Length} obstacle box(es).");
        }

        void OnDestroy()
        {
            m_Grid?.Dispose();
            m_Grid = null;
        }
    }
}
