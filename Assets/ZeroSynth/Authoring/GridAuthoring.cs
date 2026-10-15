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

        [Header("Spatial grid (neighbour queries)")]
        [Tooltip("Cell size for neighbour queries. Set near the largest query radius in use — too small and queries sweep many cells, too large and each cell holds too many candidates.")]
        [SerializeField, Min(0.5f)] float m_SpatialCellSize = 4f;

        [Tooltip("Must be at least the agent pool size, or agents are dropped from queries.")]
        [SerializeField, Min(1)] int m_SpatialCapacity = 8192;

        EntityManager m_EntityManager;
        GridData m_Grid;
        SpatialGridData m_Spatial;
        Entity m_Entity;
        Entity m_SpatialEntity;

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

            CreateSpatialGrid(config);

            Debug.Log($"[ZeroSynth] Grid {m_Width}x{m_Height} @ {m_CellSize}m created.");
        }

        /// <summary>
        /// The neighbour-query grid covers the same world area as the
        /// pathfinding grid but at a coarser cell size.
        /// </summary>
        void CreateSpatialGrid(GridConfig pathConfig)
        {
            float worldWidth = m_Width * m_CellSize;
            float worldHeight = m_Height * m_CellSize;

            var spatialConfig = new SpatialGridConfig
            {
                Origin = pathConfig.Origin,
                CellSize = m_SpatialCellSize,
                Width = Mathf.Max(1, Mathf.CeilToInt(worldWidth / m_SpatialCellSize)),
                Height = Mathf.Max(1, Mathf.CeilToInt(worldHeight / m_SpatialCellSize))
            };

            m_Spatial = new SpatialGridData();
            m_Spatial.Allocate(spatialConfig.CellCount, m_SpatialCapacity);

            m_SpatialEntity = m_EntityManager.CreateEntity();
            m_EntityManager.SetName(m_SpatialEntity, "SpatialGrid");
            m_EntityManager.AddComponentData(m_SpatialEntity, spatialConfig);
            m_EntityManager.AddComponentObject(m_SpatialEntity, m_Spatial);

            Debug.Log($"[ZeroSynth] Spatial grid {spatialConfig.Width}x{spatialConfig.Height} @ {m_SpatialCellSize}m, capacity {m_SpatialCapacity}.");
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
            m_Spatial?.Dispose();
            m_Spatial = null;
        }
    }
}
