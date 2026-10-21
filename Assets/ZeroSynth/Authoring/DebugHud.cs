using System.Text;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace ZeroSynth.Authoring
{
    /// <summary>
    /// On-screen performance and sim readout. Works on device, unlike the
    /// gizmo-based grid view, so this is the only window into a build running
    /// on a phone.
    /// </summary>
    /// <remarks>
    /// IMGUI on purpose: no scene wiring, no canvas, no prefab to keep in sync
    /// — drop the component on anything and it works. The label string is
    /// rebuilt on a slow interval rather than per frame so the overlay does not
    /// distort the numbers it is reporting.
    /// </remarks>
    public sealed class DebugHud : MonoBehaviour
    {
        [Header("Display")]
        [SerializeField] bool m_Visible = true;

        [Tooltip("How often the readout text is rebuilt. Frame timing is still sampled every frame.")]
        [SerializeField, Min(0.05f)] float m_RefreshInterval = 0.25f;

        [Tooltip("Font size at 1080p. Scaled for the actual screen height.")]
        [SerializeField, Min(8)] int m_BaseFontSize = 18;

        [Header("Frame rate")]
        [Tooltip("Applied at startup. -1 leaves the platform default alone.")]
        [SerializeField] int m_TargetFrameRate = -1;

        [Tooltip("Sample window for the 1% low figure.")]
        [SerializeField, Min(30)] int m_SampleCount = 300;

        float[] m_Samples;
        int m_SampleHead;
        int m_SamplesFilled;

        float m_Accumulated;
        int m_AccumulatedFrames;
        float m_NextRefresh;

        float m_Fps;
        float m_FrameMs;
        float m_WorstMs;
        float m_OnePercentLowFps;

        EntityManager m_EntityManager;
        EntityQuery m_ActiveAgents;
        EntityQuery m_AllAgents;
        EntityQuery m_GridQuery;
        EntityQuery m_RunQuery;
        EntityQuery m_PoweredTurrets;
        EntityQuery m_AllTurrets;
        bool m_EcsReady;

        readonly StringBuilder m_Builder = new StringBuilder(512);
        string m_Text = "";
        GUIStyle m_Style;

        void Start()
        {
            if (m_TargetFrameRate > 0)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = m_TargetFrameRate;
            }

            m_Samples = new float[m_SampleCount];

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            m_EntityManager = world.EntityManager;

            m_ActiveAgents = m_EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<HordeAgent>(),
                ComponentType.ReadOnly<Active>());

            // IgnoreComponentEnabledState counts pooled agents too, so the
            // readout shows live-vs-pool rather than just live.
            m_AllAgents = m_EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<HordeAgent>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState
            });

            m_GridQuery = m_EntityManager.CreateEntityQuery(ComponentType.ReadOnly<GridData>());
            m_RunQuery = m_EntityManager.CreateEntityQuery(ComponentType.ReadOnly<WaveState>());

            m_PoweredTurrets = m_EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Turret>(),
                ComponentType.ReadOnly<Powered>());

            m_AllTurrets = m_EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Turret>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState
            });

            m_EcsReady = true;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            m_Accumulated += dt;
            m_AccumulatedFrames++;

            m_Samples[m_SampleHead] = dt;
            m_SampleHead = (m_SampleHead + 1) % m_Samples.Length;
            if (m_SamplesFilled < m_Samples.Length) m_SamplesFilled++;

            if (Time.unscaledTime < m_NextRefresh) return;
            m_NextRefresh = Time.unscaledTime + m_RefreshInterval;

            m_Fps = m_AccumulatedFrames / m_Accumulated;
            m_FrameMs = (m_Accumulated / m_AccumulatedFrames) * 1000f;
            m_Accumulated = 0f;
            m_AccumulatedFrames = 0;

            ComputeLows();
            Rebuild();
        }

        /// <summary>
        /// Worst frame and the 1% low. The average hides exactly the hitches
        /// that make a horde game feel bad, so both are worth the scan.
        /// </summary>
        void ComputeLows()
        {
            if (m_SamplesFilled == 0) return;

            float worst = 0f;
            for (int i = 0; i < m_SamplesFilled; i++)
                if (m_Samples[i] > worst) worst = m_Samples[i];
            m_WorstMs = worst * 1000f;

            // Selection rather than a full sort: we only need the threshold
            // that the slowest one percent of frames exceed.
            int rank = Mathf.Max(1, Mathf.RoundToInt(m_SamplesFilled * 0.01f));
            float threshold = 0f;
            for (int pass = 0; pass < rank; pass++)
            {
                float best = 0f;
                for (int i = 0; i < m_SamplesFilled; i++)
                {
                    float v = m_Samples[i];
                    if (v > best && (pass == 0 || v < threshold)) best = v;
                }
                threshold = best;
            }

            m_OnePercentLowFps = threshold > 0f ? 1f / threshold : 0f;
        }

        void Rebuild()
        {
            m_Builder.Clear();

            m_Builder.Append("FPS ").Append(m_Fps.ToString("F1"))
                     .Append("   ").Append(m_FrameMs.ToString("F2")).Append(" ms\n");
            m_Builder.Append("1% low ").Append(m_OnePercentLowFps.ToString("F1"))
                     .Append("   worst ").Append(m_WorstMs.ToString("F1")).Append(" ms\n");

            if (m_EcsReady)
            {
                int active = m_ActiveAgents.CalculateEntityCount();
                int pool = m_AllAgents.CalculateEntityCount();
                m_Builder.Append("agents ").Append(active).Append(" / ").Append(pool).Append('\n');

                m_Builder.Append("turrets ").Append(m_PoweredTurrets.CalculateEntityCount())
                         .Append(" powered / ").Append(m_AllTurrets.CalculateEntityCount()).Append('\n');

                if (!m_RunQuery.IsEmpty)
                {
                    var runEntity = m_RunQuery.GetSingletonEntity();
                    var wave = m_EntityManager.GetComponentData<WaveState>(runEntity);
                    var economy = m_EntityManager.GetComponentData<Economy>(runEntity);

                    m_Builder.Append("wave ").Append(wave.Index).Append("  ").Append(wave.Phase);
                    if (wave.Phase == WavePhase.Preparing)
                        m_Builder.Append(" ").Append(math.max(0f, wave.Timer).ToString("F1")).Append("s");
                    else if (wave.Phase == WavePhase.Spawning)
                        m_Builder.Append("  ").Append(wave.Released).Append("+").Append(wave.Remaining);
                    m_Builder.Append('\n');

                    m_Builder.Append("biomass ").Append(economy.Biomass.ToString("F0"))
                             .Append("   scrap ").Append(economy.MetalScrap.ToString("F0")).Append('\n');
                }

                if (!m_GridQuery.IsEmpty)
                {
                    var grid = m_EntityManager.GetComponentObject<GridData>(m_GridQuery.GetSingletonEntity());
                    m_Builder.Append("field gen ").Append(grid.Generation)
                             .Append(grid.Dirty ? " (dirty)" : "").Append('\n');
                }
                else
                {
                    m_Builder.Append("field —\n");
                }
            }
            else
            {
                m_Builder.Append("ecs world not available\n");
            }

            // Confirms the device actually got Vulkan rather than silently
            // falling back, which the project settings alone cannot tell you.
            m_Builder.Append(SystemInfo.graphicsDeviceType)
                     .Append("  ").Append(Screen.width).Append('x').Append(Screen.height)
                     .Append('\n');
            m_Builder.Append(SystemInfo.deviceModel);

            m_Text = m_Builder.ToString();
        }

        void OnGUI()
        {
            if (!m_Visible || m_Text.Length == 0) return;

            if (m_Style == null)
            {
                m_Style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperLeft,
                    richText = false
                };
                m_Style.normal.textColor = Color.white;
            }

            m_Style.fontSize = Mathf.Max(10, Mathf.RoundToInt(m_BaseFontSize * (Screen.height / 1080f)));

            float pad = m_Style.fontSize * 0.6f;
            var size = m_Style.CalcSize(new GUIContent(m_Text));
            var box = new Rect(pad, pad, size.x + pad * 2f, size.y + pad * 2f);

            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = previous;

            GUI.Label(new Rect(box.x + pad, box.y + pad, size.x, size.y), m_Text, m_Style);
        }

        public void SetVisible(bool visible) => m_Visible = visible;

        void OnDestroy()
        {
            if (!m_EcsReady) return;
            m_ActiveAgents.Dispose();
            m_AllAgents.Dispose();
            m_GridQuery.Dispose();
            m_RunQuery.Dispose();
            m_PoweredTurrets.Dispose();
            m_AllTurrets.Dispose();
        }
    }
}
