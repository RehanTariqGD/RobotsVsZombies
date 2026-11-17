using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ZeroSynth.UI
{
    /// <summary>Converts physical notch insets to panel units so PanelSettings scaling stays safe.</summary>
    public sealed class SafeAreaPadding : IDisposable
    {
        readonly VisualElement m_Root;
        Rect m_LastSafeArea;
        Vector2 m_LastSize;
        Vector2Int m_LastScreen;
        bool m_Applied;

        public SafeAreaPadding(VisualElement root)
        {
            m_Root = root;
            m_Root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            Update();
        }

        public void Update()
        {
            var safe = Screen.safeArea;
            var size = m_Root.worldBound.size;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (screen.x <= 0 || screen.y <= 0 || !(size.x > 0f) || !(size.y > 0f)) return;
            if (m_Applied && safe == m_LastSafeArea && size == m_LastSize && screen == m_LastScreen) return;

            m_LastSafeArea = safe;
            m_LastSize = size;
            m_LastScreen = screen;
            m_Applied = true;
            float scaleX = size.x / screen.x;
            float scaleY = size.y / screen.y;
            m_Root.style.paddingLeft = Mathf.Max(0f, safe.xMin) * scaleX;
            m_Root.style.paddingRight = Mathf.Max(0f, screen.x - safe.xMax) * scaleX;
            m_Root.style.paddingTop = Mathf.Max(0f, screen.y - safe.yMax) * scaleY;
            m_Root.style.paddingBottom = Mathf.Max(0f, safe.yMin) * scaleY;
        }

        void OnGeometryChanged(GeometryChangedEvent evt) => Update();

        public void Dispose() => m_Root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
    }
}
