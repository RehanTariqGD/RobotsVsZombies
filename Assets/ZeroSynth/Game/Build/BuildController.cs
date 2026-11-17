using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using ZeroSynth.Authoring;
using ZeroSynth.Flow;

namespace ZeroSynth.Build
{
    /// <summary>One entry in the build bar.</summary>
    public readonly struct BuildOption
    {
        public readonly StructureKind Kind;
        public readonly string Label;
        public readonly float Cost;

        public BuildOption(StructureKind kind, string label, float cost)
        {
            Kind = kind;
            Label = label;
            Cost = cost;
        }
    }

    /// <summary>
    /// Places structures on the grid for scrap.
    /// </summary>
    /// <remarks>
    /// UI talks to this and never to the ECS world: it reads <see cref="Options"/>,
    /// calls <see cref="Select"/> and <see cref="Cancel"/>, and listens to
    /// <see cref="SelectionChanged"/> and <see cref="Message"/>.
    ///
    /// Selecting a structure engages the tactical pause, as in the GDD, so the
    /// player can place walls against a breach without the breach getting worse.
    /// Selection persists across placements, so several can be laid in one go.
    /// </remarks>
    public sealed class BuildController : MonoBehaviour
    {
        public static BuildController Instance { get; private set; }

        [SerializeField] Camera m_Camera;
        [SerializeField] BuildDefinition[] m_Definitions = new BuildDefinition[0];

        [Tooltip("Documents whose buttons must swallow taps, so pressing the build bar never places anything behind it.")]
        [SerializeField] UIDocument[] m_BlockingDocuments = new UIDocument[0];

        [SerializeField] float m_GroundHeight;

        static readonly Color s_Valid = new Color(0.2f, 1f, 0.45f, 0.95f);
        static readonly Color s_Invalid = new Color(1f, 0.25f, 0.25f, 0.95f);

        readonly List<BuildOption> m_Options = new List<BuildOption>();

        EntityManager m_EntityManager;
        EntityQuery m_EconomyQuery;
        GridAuthoring m_Grid;
        LineRenderer m_Outline;
        BuildDefinition m_Active;
        bool m_Bound;
        bool m_EngagedPause;
        bool m_PressBlocked;

        public IReadOnlyList<BuildOption> Options => m_Options;
        public bool HasSelection { get; private set; }
        public StructureKind Selected { get; private set; }

        /// <summary>Raised when the selection changes or is cleared.</summary>
        public event Action SelectionChanged;

        /// <summary>Short upper-case feedback for the UI, e.g. "NOT ENOUGH SCRAP".</summary>
        public event Action<string> Message;

        void Awake()
        {
            Instance = this;

            foreach (var def in m_Definitions)
                m_Options.Add(new BuildOption(def.Kind, def.Label, StructureCosts.Scrap(def.Kind)));

            var go = new GameObject("BuildOutline");
            go.transform.SetParent(transform, false);
            m_Outline = go.AddComponent<LineRenderer>();
            m_Outline.useWorldSpace = true;
            m_Outline.loop = true;
            m_Outline.positionCount = 4;
            m_Outline.widthMultiplier = 0.18f;
            m_Outline.material = new Material(Shader.Find("Sprites/Default"));
            m_Outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Outline.enabled = false;
        }

        void OnDestroy()
        {
            Release();
            if (Instance == this) Instance = null;
        }

        /// <summary>True if the player has enough scrap for this kind.</summary>
        public bool CanAfford(StructureKind kind)
        {
            return Bind() && ReadScrap() >= StructureCosts.Scrap(kind);
        }

        public void Select(StructureKind kind)
        {
            var def = Find(kind);
            if (def == null) return;

            m_Active = def;
            Selected = kind;
            HasSelection = true;
            PossessionController.BuildModeActive = true;

            var flow = GameFlow.Instance;
            if (flow != null && !flow.TacticalPause)
            {
                m_EngagedPause = true;
                flow.SetTacticalPause(true);
            }

            SelectionChanged?.Invoke();
        }

        public void Cancel()
        {
            if (!HasSelection) return;
            Release();
            SelectionChanged?.Invoke();
        }

        void Release()
        {
            HasSelection = false;
            m_Active = null;
            PossessionController.BuildModeActive = false;
            if (m_Outline != null) m_Outline.enabled = false;

            // Only undo a pause this controller started. If the player had
            // already frozen the game themselves, leave it frozen.
            var flow = GameFlow.Instance;
            if (m_EngagedPause && flow != null)
                flow.SetTacticalPause(false);
            m_EngagedPause = false;
        }

        void Update()
        {
            if (!HasSelection) return;

            var flow = GameFlow.Instance;
            if (flow != null && flow.State != GameState.Playing)
            {
                Cancel();
                return;
            }

            if (!Bind()) return;

            var pointer = Pointer.current;
            if (pointer == null) return;

            Vector2 screen = pointer.position.ReadValue();

            // A press that began on the build bar must not place on release,
            // even if the finger slid onto the ground first.
            if (pointer.press.wasPressedThisFrame)
                m_PressBlocked = IsOverBlockingUi(screen);

            if (!TryGetGroundPoint(screen, out float3 ground))
                return;

            var config = m_Grid.Config;
            var footprint = new int2(m_Active.Footprint.x, m_Active.Footprint.y);
            int2 anchor = config.AnchorForCentre(ground, footprint);

            bool valid = IsBuildable(config, anchor, footprint, out string reason);
            DrawOutline(config, anchor, footprint, valid);

            if (pointer.press.wasReleasedThisFrame && !m_PressBlocked)
                TryPlace(config, anchor, valid, reason);
        }

        void TryPlace(GridConfig config, int2 anchor, bool valid, string reason)
        {
            if (!valid)
            {
                Message?.Invoke(reason);
                return;
            }

            float cost = StructureCosts.Scrap(m_Active.Kind);
            if (ReadScrap() < cost)
            {
                Message?.Invoke("NOT ENOUGH SCRAP");
                return;
            }

            var economyEntity = m_EconomyQuery.GetSingletonEntity();
            var economy = m_EntityManager.GetComponentData<Economy>(economyEntity);
            economy.MetalScrap -= cost;
            m_EntityManager.SetComponentData(economyEntity, economy);

            StructureSpawner.Spawn(m_EntityManager, config, m_Active, anchor);
            Message?.Invoke(m_Active.Label.ToUpperInvariant() + " BUILT");
        }

        /// <summary>
        /// Every cell of the footprint must be on the map and empty: no terrain,
        /// no structure, nothing reserved.
        /// </summary>
        bool IsBuildable(GridConfig config, int2 anchor, int2 footprint, out string reason)
        {
            reason = null;
            var flags = m_Grid.Grid.Flags;

            for (int y = 0; y < footprint.y; y++)
            for (int x = 0; x < footprint.x; x++)
            {
                int cx = anchor.x + x;
                int cy = anchor.y + y;

                if (!config.InBounds(cx, cy))
                {
                    reason = "OUT OF BOUNDS";
                    return false;
                }

                if (flags[config.Index(cx, cy)] != CellFlags.None)
                {
                    reason = "SPACE BLOCKED";
                    return false;
                }
            }

            return true;
        }

        void DrawOutline(GridConfig config, int2 anchor, int2 footprint, bool valid)
        {
            float3 centre = config.FootprintCentre(anchor, footprint);
            float hx = footprint.x * config.CellSize * 0.5f;
            float hz = footprint.y * config.CellSize * 0.5f;
            float y = m_GroundHeight + 0.15f;

            m_Outline.SetPosition(0, new Vector3(centre.x - hx, y, centre.z - hz));
            m_Outline.SetPosition(1, new Vector3(centre.x + hx, y, centre.z - hz));
            m_Outline.SetPosition(2, new Vector3(centre.x + hx, y, centre.z + hz));
            m_Outline.SetPosition(3, new Vector3(centre.x - hx, y, centre.z + hz));
            m_Outline.startColor = m_Outline.endColor = valid ? s_Valid : s_Invalid;
            m_Outline.enabled = true;
        }

        bool IsOverBlockingUi(Vector2 screen)
        {
            foreach (var doc in m_BlockingDocuments)
            {
                if (doc == null) continue;

                var root = doc.rootVisualElement;
                if (root == null || root.panel == null) continue;

                // UI Toolkit positions are top-left origin; the pointer is bottom-left.
                var panelPoint = RuntimePanelUtils.ScreenToPanel(root.panel,
                    new Vector2(screen.x, Screen.height - screen.y));

                for (var element = root.panel.Pick(panelPoint); element != null; element = element.parent)
                {
                    if (element is Button || element.ClassListContains("blocks-input"))
                        return true;
                }
            }

            return false;
        }

        bool TryGetGroundPoint(Vector2 screen, out float3 result)
        {
            result = float3.zero;
            if (m_Camera == null) m_Camera = Camera.main;
            if (m_Camera == null) return false;

            var ray = m_Camera.ScreenPointToRay(screen);
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return false;

            float t = (m_GroundHeight - ray.origin.y) / ray.direction.y;
            if (t < 0f) return false;

            Vector3 hit = ray.origin + ray.direction * t;
            result = new float3(hit.x, m_GroundHeight, hit.z);
            return true;
        }

        BuildDefinition Find(StructureKind kind)
        {
            foreach (var def in m_Definitions)
                if (def.Kind == kind) return def;
            return null;
        }

        float ReadScrap()
        {
            if (m_EconomyQuery.IsEmpty) return 0f;
            return m_EntityManager.GetComponentData<Economy>(m_EconomyQuery.GetSingletonEntity()).MetalScrap;
        }

        /// <summary>
        /// Lazily connects to the world and grid. Both are rebuilt between runs,
        /// so this re-checks the world on every call rather than caching once.
        /// </summary>
        bool Bind()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                m_Bound = false;
                return false;
            }

            if (m_Grid == null)
                m_Grid = FindFirstObjectByType<GridAuthoring>();
            if (m_Grid == null || m_Grid.Grid == null)
                return false;

            if (!m_Bound || m_EntityManager.World != world)
            {
                m_EntityManager = world.EntityManager;
                m_EconomyQuery = m_EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Economy>());
                m_Bound = true;
            }

            return !m_EconomyQuery.IsEmpty;
        }
    }
}
