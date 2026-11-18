using UnityEngine;
using UnityEngine.UIElements;
using ZeroSynth.Flow;

namespace ZeroSynth.UI
{
    /// <summary>
    /// Marks where the next wave will come from, so the player can place
    /// defences before it starts.
    /// </summary>
    /// <remarks>
    /// One badge per front, pinned to the ground point on the spawn ring where
    /// that front is centred. When that point is off screen the badge slides
    /// to the screen edge along the line towards it, which works for any camera
    /// framing without knowing which way the map is rotated. It adds its own
    /// elements to the build overlay's root, like the tutorial prompts, so
    /// there is no UXML to keep in step.
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class FrontIndicators : MonoBehaviour
    {
        const int MaxFronts = 3;
        const float BadgeSize = 36f;

        [Tooltip("Keep badges this far in from the top and sides, in panel pixels.")]
        [SerializeField, Min(0f)] float m_EdgeMargin = 56f;

        [Tooltip("Keep badges this far above the bottom edge, clear of the build bar.")]
        [SerializeField, Min(0f)] float m_BottomMargin = 130f;

        VisualElement m_Root;
        Label[] m_Badges;
        Camera m_Camera;

        void OnEnable()
        {
            m_Root = GetComponent<UIDocument>().rootVisualElement;
            m_Badges = new Label[MaxFronts];

            for (int i = 0; i < MaxFronts; i++)
            {
                var badge = new Label("!");
                badge.pickingMode = PickingMode.Ignore;

                var style = badge.style;
                style.position = Position.Absolute;
                style.width = BadgeSize;
                style.height = BadgeSize;
                style.borderTopLeftRadius = BadgeSize * 0.5f;
                style.borderTopRightRadius = BadgeSize * 0.5f;
                style.borderBottomLeftRadius = BadgeSize * 0.5f;
                style.borderBottomRightRadius = BadgeSize * 0.5f;
                style.borderTopWidth = 3f;
                style.borderBottomWidth = 3f;
                style.borderLeftWidth = 3f;
                style.borderRightWidth = 3f;
                var edge = new Color(1f, 0.85f, 0.8f, 0.95f);
                style.borderTopColor = edge;
                style.borderBottomColor = edge;
                style.borderLeftColor = edge;
                style.borderRightColor = edge;
                style.backgroundColor = new Color(0.85f, 0.15f, 0.1f, 0.85f);
                style.color = Color.white;
                style.fontSize = 22f;
                style.unityFontStyleAndWeight = FontStyle.Bold;
                style.unityTextAlign = TextAnchor.MiddleCenter;
                style.display = DisplayStyle.None;

                m_Root.Add(badge);
                m_Badges[i] = badge;
            }
        }

        void OnDisable()
        {
            if (m_Badges == null) return;

            foreach (var badge in m_Badges)
                badge?.RemoveFromHierarchy();

            m_Badges = null;
        }

        void LateUpdate()
        {
            if (m_Badges == null) return;

            var flow = GameFlow.Instance;
            var snapshot = flow != null ? flow.Snapshot : default;

            bool show = flow != null
                        && flow.State == GameState.Playing
                        && snapshot.Valid
                        && snapshot.Fronts.Arc > 0f
                        && (snapshot.Phase == WavePhase.Preparing || snapshot.Phase == WavePhase.Spawning);

            if (!show || m_Root.panel == null)
            {
                HideAll(0);
                return;
            }

            if (m_Camera == null)
                m_Camera = Camera.main;
            if (m_Camera == null)
            {
                HideAll(0);
                return;
            }

            float width = m_Root.resolvedStyle.width;
            float height = m_Root.resolvedStyle.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width < 1f || height < 1f)
            {
                HideAll(0);
                return;
            }

            // Pulses while there is time to react, then holds steady once the
            // wave is actually pouring in.
            float alpha = snapshot.Phase == WavePhase.Preparing
                ? 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 5f)
                : 0.9f;

            int count = Mathf.Min(snapshot.Fronts.Count, MaxFronts);
            for (int i = 0; i < count; i++)
            {
                float angle = snapshot.Fronts.Angle[i];
                var world = new Vector3(
                    snapshot.FrontCentre.x + Mathf.Cos(angle) * snapshot.FrontRadius,
                    0f,
                    snapshot.FrontCentre.z + Mathf.Sin(angle) * snapshot.FrontRadius);

                Vector2 point = RuntimePanelUtils.CameraTransformWorldToPanel(m_Root.panel, world, m_Camera);
                point = ClampToScreen(point, width, height);

                var badge = m_Badges[i];
                badge.style.left = point.x - BadgeSize * 0.5f;
                badge.style.top = point.y - BadgeSize * 0.5f;
                badge.style.opacity = alpha;
                badge.style.display = DisplayStyle.Flex;
            }

            HideAll(count);
        }

        /// <summary>
        /// Pulls a point that is off screen in along the line from the middle
        /// of the screen, so the badge keeps pointing the right way.
        /// </summary>
        Vector2 ClampToScreen(Vector2 point, float width, float height)
        {
            float left = m_EdgeMargin;
            float right = width - m_EdgeMargin;
            float top = m_EdgeMargin;
            float bottom = height - m_BottomMargin;

            if (point.x >= left && point.x <= right && point.y >= top && point.y <= bottom)
                return point;

            var centre = new Vector2(width * 0.5f, height * 0.5f);
            var offset = point - centre;

            // The centre is not the middle of the allowed box when the bottom
            // margin differs from the top, so scale each axis against its own
            // side.
            float limitX = offset.x >= 0f ? right - centre.x : centre.x - left;
            float limitY = offset.y >= 0f ? bottom - centre.y : centre.y - top;
            limitX = Mathf.Max(1f, limitX);
            limitY = Mathf.Max(1f, limitY);

            float scale = Mathf.Min(1f,
                Mathf.Min(limitX / Mathf.Max(0.001f, Mathf.Abs(offset.x)),
                          limitY / Mathf.Max(0.001f, Mathf.Abs(offset.y))));

            return centre + offset * scale;
        }

        void HideAll(int from)
        {
            for (int i = from; i < m_Badges.Length; i++)
            {
                if (m_Badges[i].style.display != DisplayStyle.None)
                    m_Badges[i].style.display = DisplayStyle.None;
            }
        }
    }
}
