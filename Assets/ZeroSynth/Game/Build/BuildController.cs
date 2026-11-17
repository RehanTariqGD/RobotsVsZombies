using System;
using System.Collections.Generic;
using Unity.Collections;
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

    public enum BuildMode
    {
        None,

        /// <summary>A structure is selected and a ground tap places it.</summary>
        Place,

        /// <summary>A tap on a structure restores it to full health for scrap.</summary>
        Repair,

        /// <summary>A tap on a structure removes it and refunds half its cost.</summary>
        Sell,
    }

    /// <summary>
    /// Places, repairs and sells structures, all for scrap.
    /// </summary>
    /// <remarks>
    /// UI talks to this and never to the ECS world: it reads <see cref="Options"/>,
    /// calls <see cref="Select"/>, <see cref="SelectRepair"/>, <see cref="SelectSell"/>,
    /// <see cref="Rotate"/> and <see cref="Cancel"/>, and listens to
    /// <see cref="SelectionChanged"/> and <see cref="Message"/>.
    ///
    /// Entering any mode engages the tactical pause, as in the GDD, so the player
    /// can work on a breach without it getting worse. The mode persists across
    /// actions, so several can be done in one go.
    /// </remarks>
    public sealed class BuildController : MonoBehaviour
    {
        public static BuildController Instance { get; private set; }

        [SerializeField] Camera m_Camera;
        [SerializeField] BuildDefinition[] m_Definitions = new BuildDefinition[0];

        [Tooltip("Documents whose buttons must swallow taps, so pressing the build bar never acts on the world behind it.")]
        [SerializeField] UIDocument[] m_BlockingDocuments = new UIDocument[0];

        [SerializeField] float m_GroundHeight;

        [Tooltip("Scrap per point of missing health when repairing.")]
        [SerializeField, Min(0f)] float m_RepairCostPerHealth = 0.15f;

        [Tooltip("Fraction of the build cost returned when selling.")]
        [SerializeField, Range(0f, 1f)] float m_SellRefund = 0.5f;

        static readonly Color s_Valid = new Color(0.2f, 1f, 0.45f, 0.95f);
        static readonly Color s_Invalid = new Color(1f, 0.25f, 0.25f, 0.95f);
        static readonly Color s_Sell = new Color(1f, 0.65f, 0.15f, 0.95f);

        readonly List<BuildOption> m_Options = new List<BuildOption>();

        EntityManager m_EntityManager;
        EntityQuery m_EconomyQuery;
        EntityQuery m_StructureQuery;
        GridAuthoring m_Grid;
        LineRenderer m_Outline;
        BuildDefinition m_Active;
        bool m_Bound;
        bool m_EngagedPause;
        bool m_PressBlocked;

        public IReadOnlyList<BuildOption> Options => m_Options;

        public BuildMode Mode { get; private set; }

        /// <summary>Any mode is active. Kept for UI that only needs to know whether to show Cancel.</summary>
        public bool HasSelection => Mode != BuildMode.None;

        /// <summary>The structure being placed. Meaningful only in <see cref="BuildMode.Place"/>.</summary>
        public StructureKind Selected { get; private set; }

        /// <summary>The placement is turned a quarter turn, swapping its footprint's axes.</summary>
        public bool Rotated { get; private set; }

        /// <summary>Rotating would change something: placing a structure with a non-square footprint.</summary>
        public bool CanRotate => Mode == BuildMode.Place && m_Active != null && m_Active.Footprint.x != m_Active.Footprint.y;

        /// <summary>Raised when the mode, selection or rotation changes.</summary>
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
            Rotated = false;
            Enter(BuildMode.Place);
        }

        public void SelectRepair()
        {
            m_Active = null;
            Enter(BuildMode.Repair);
        }

        public void SelectSell()
        {
            m_Active = null;
            Enter(BuildMode.Sell);
        }

        /// <summary>Turns the structure being placed. Walls and foundries are not square.</summary>
        public void Rotate()
        {
            if (!CanRotate) return;
            Rotated = !Rotated;
            SelectionChanged?.Invoke();
        }

        public void Cancel()
        {
            if (!HasSelection) return;
            Release();
            SelectionChanged?.Invoke();
        }

        void Enter(BuildMode mode)
        {
            Mode = mode;
            PossessionController.BuildModeActive = true;

            var flow = GameFlow.Instance;
            if (flow != null && !flow.TacticalPause)
            {
                m_EngagedPause = true;
                flow.SetTacticalPause(true);
            }

            SelectionChanged?.Invoke();
        }

        void Release()
        {
            Mode = BuildMode.None;
            m_Active = null;
            Rotated = false;
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
            var keyboard = Keyboard.current;

            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
                Rotate();

            if (pointer == null) return;

            Vector2 screen = pointer.position.ReadValue();

            // A press that began on the build bar must not act on release, even
            // if the finger slid onto the ground first.
            if (pointer.press.wasPressedThisFrame)
                m_PressBlocked = IsOverBlockingUi(screen);

            if (!TryGetGroundPoint(screen, out float3 ground))
                return;

            bool released = pointer.press.wasReleasedThisFrame && !m_PressBlocked;

            if (Mode == BuildMode.Place)
                UpdatePlace(ground, released);
            else
                UpdateTool(ground, released);
        }

        // ---------------------------------------------------------------
        // Placing
        // ---------------------------------------------------------------

        int2 CurrentFootprint()
        {
            return Rotated
                ? new int2(m_Active.Footprint.y, m_Active.Footprint.x)
                : new int2(m_Active.Footprint.x, m_Active.Footprint.y);
        }

        void UpdatePlace(float3 ground, bool released)
        {
            var config = m_Grid.Config;
            int2 footprint = CurrentFootprint();
            int2 anchor = config.AnchorForCentre(ground, footprint);

            bool valid = IsBuildable(config, anchor, footprint, out string reason);
            DrawOutline(config, anchor, footprint, valid ? s_Valid : s_Invalid);

            if (released)
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
            if (!TrySpend(cost))
            {
                Message?.Invoke("NOT ENOUGH SCRAP");
                return;
            }

            StructureSpawner.Spawn(m_EntityManager, config, m_Active, anchor, Rotated);
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

            if (OverlapsLiveStructure(anchor, footprint))
            {
                reason = "SPACE BLOCKED";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks against the structures themselves, not the grid's flags.
        /// </summary>
        /// <remarks>
        /// The flags are written by a system that sits inside the simulation
        /// group, and the tactical pause freezes that group. Building while
        /// paused therefore left the grid looking empty: the same cells passed
        /// validation again, scrap was charged again, and structures stacked.
        /// Dead structures are ignored so a wreck can be rebuilt over.
        /// </remarks>
        bool OverlapsLiveStructure(int2 anchor, int2 footprint)
        {
            var anchors = m_StructureQuery.ToComponentDataArray<GridAnchor>(Allocator.Temp);
            var sizes = m_StructureQuery.ToComponentDataArray<Structure>(Allocator.Temp);
            var healths = m_StructureQuery.ToComponentDataArray<Health>(Allocator.Temp);

            bool overlaps = false;
            for (int i = 0; i < anchors.Length && !overlaps; i++)
            {
                if (healths[i].Current <= 0f) continue;

                int2 otherMin = anchors[i].Cell;
                int2 otherMax = otherMin + math.max(sizes[i].Footprint, new int2(1, 1));
                int2 mineMax = anchor + footprint;

                overlaps = anchor.x < otherMax.x && mineMax.x > otherMin.x
                        && anchor.y < otherMax.y && mineMax.y > otherMin.y;
            }

            anchors.Dispose();
            sizes.Dispose();
            healths.Dispose();
            return overlaps;
        }

        // ---------------------------------------------------------------
        // Repair and sell
        // ---------------------------------------------------------------

        void UpdateTool(float3 ground, bool released)
        {
            var config = m_Grid.Config;
            int2 cell = config.WorldToCell(ground);

            Entity target = FindStructureAt(cell, out int2 anchor, out int2 size);

            if (target == Entity.Null)
            {
                DrawOutline(config, cell, new int2(1, 1), s_Invalid);
                if (released) Message?.Invoke("NO STRUCTURE");
                return;
            }

            DrawOutline(config, anchor, size, Mode == BuildMode.Sell ? s_Sell : s_Valid);

            if (!released) return;

            if (Mode == BuildMode.Repair) Repair(target);
            else Sell(target);
        }

        /// <summary>The live structure covering a cell, if any.</summary>
        Entity FindStructureAt(int2 cell, out int2 anchor, out int2 size)
        {
            anchor = default;
            size = default;

            var entities = m_StructureQuery.ToEntityArray(Allocator.Temp);
            var anchors = m_StructureQuery.ToComponentDataArray<GridAnchor>(Allocator.Temp);
            var sizes = m_StructureQuery.ToComponentDataArray<Structure>(Allocator.Temp);
            var healths = m_StructureQuery.ToComponentDataArray<Health>(Allocator.Temp);

            Entity found = Entity.Null;
            for (int i = 0; i < entities.Length; i++)
            {
                if (healths[i].Current <= 0f) continue;

                int2 min = anchors[i].Cell;
                int2 extent = math.max(sizes[i].Footprint, new int2(1, 1));
                int2 max = min + extent;

                if (cell.x < min.x || cell.x >= max.x || cell.y < min.y || cell.y >= max.y)
                    continue;

                found = entities[i];
                anchor = min;
                size = extent;
                break;
            }

            entities.Dispose();
            anchors.Dispose();
            sizes.Dispose();
            healths.Dispose();
            return found;
        }

        void Repair(Entity entity)
        {
            var health = m_EntityManager.GetComponentData<Health>(entity);
            float missing = health.Max - health.Current;

            if (missing < 0.5f)
            {
                Message?.Invoke("NOTHING TO REPAIR");
                return;
            }

            float cost = Mathf.Max(1f, Mathf.Ceil(missing * m_RepairCostPerHealth));
            if (!TrySpend(cost))
            {
                Message?.Invoke("NOT ENOUGH SCRAP");
                return;
            }

            health.Current = health.Max;
            m_EntityManager.SetComponentData(entity, health);
            Message?.Invoke("REPAIRED");
        }

        void Sell(Entity entity)
        {
            var structure = m_EntityManager.GetComponentData<Structure>(entity);
            float refund = Mathf.Floor(StructureCosts.Scrap(structure.Kind) * m_SellRefund);

            var economyEntity = m_EconomyQuery.GetSingletonEntity();
            var economy = m_EntityManager.GetComponentData<Economy>(economyEntity);
            economy.MetalScrap += refund;
            m_EntityManager.SetComponentData(economyEntity, economy);

            // The grid, the power network and the wall costs all notice a
            // structure vanishing: each rebuilds when its signature changes.
            m_EntityManager.DestroyEntity(entity);
            Message?.Invoke("SOLD +" + refund.ToString("0"));
        }

        // ---------------------------------------------------------------
        // Shared
        // ---------------------------------------------------------------

        bool TrySpend(float cost)
        {
            if (ReadScrap() < cost)
                return false;

            var economyEntity = m_EconomyQuery.GetSingletonEntity();
            var economy = m_EntityManager.GetComponentData<Economy>(economyEntity);
            economy.MetalScrap -= cost;
            m_EntityManager.SetComponentData(economyEntity, economy);
            return true;
        }

        void DrawOutline(GridConfig config, int2 anchor, int2 footprint, Color colour)
        {
            float3 centre = config.FootprintCentre(anchor, footprint);
            float hx = footprint.x * config.CellSize * 0.5f;
            float hz = footprint.y * config.CellSize * 0.5f;
            float y = m_GroundHeight + 0.15f;

            m_Outline.SetPosition(0, new Vector3(centre.x - hx, y, centre.z - hz));
            m_Outline.SetPosition(1, new Vector3(centre.x + hx, y, centre.z - hz));
            m_Outline.SetPosition(2, new Vector3(centre.x + hx, y, centre.z + hz));
            m_Outline.SetPosition(3, new Vector3(centre.x - hx, y, centre.z + hz));
            m_Outline.startColor = m_Outline.endColor = colour;
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
                m_StructureQuery = m_EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<GridAnchor>(),
                    ComponentType.ReadOnly<Structure>(),
                    ComponentType.ReadOnly<Health>());
                m_Bound = true;
            }

            return !m_EconomyQuery.IsEmpty;
        }
    }
}
