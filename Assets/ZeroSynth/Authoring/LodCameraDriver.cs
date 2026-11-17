using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace ZeroSynth.Authoring
{
    /// <summary>
    /// Publishes the camera to the LOD system every frame.
    /// </summary>
    /// <remarks>
    /// Thresholds are projected pixel heights. At the tactical camera a Runner
    /// is only ~17 px tall, so it sits in LOD3; the full-detail tiers matter
    /// when the mech camera brings units close, and for the Titan, which is
    /// five times a Runner's height. Tune the thresholds on a real device: they
    /// trade vertex cost against how blocky distant units look.
    ///
    /// Assumes a perspective camera. An orthographic one has no distance
    /// falloff, so every agent would land in one tier.
    /// </remarks>
    public sealed class LodCameraDriver : MonoBehaviour
    {
        [SerializeField] Camera m_Camera;

        [Header("Projected height in pixels, at or above which a tier is used")]
        [SerializeField, Min(1f)] float m_Lod0Pixels = 90f;
        [SerializeField, Min(1f)] float m_Lod1Pixels = 45f;
        [SerializeField, Min(1f)] float m_Lod2Pixels = 20f;

        EntityManager m_EntityManager;
        Entity m_Entity;
        bool m_Ready;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            if (m_Camera == null) m_Camera = Camera.main;
            if (m_Camera == null)
            {
                Debug.LogError("[ZeroSynth] LodCameraDriver needs a camera.", this);
                return;
            }

            if (m_Camera.orthographic)
                Debug.LogWarning("[ZeroSynth] LOD selection assumes a perspective camera.", this);

            m_EntityManager = world.EntityManager;
            m_Entity = m_EntityManager.CreateEntity();
            m_EntityManager.SetName(m_Entity, "LodView");
            m_EntityManager.AddComponentData(m_Entity, new LodView());
            m_Ready = true;
        }

        void LateUpdate()
        {
            if (!m_Ready || !m_EntityManager.Exists(m_Entity)) return;

            float halfFov = m_Camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float pixelsPerMeter = Screen.height / (2f * Mathf.Tan(halfFov));

            m_EntityManager.SetComponentData(m_Entity, new LodView
            {
                CameraPosition = m_Camera.transform.position,
                PixelsPerMeter = pixelsPerMeter,
                Lod0Pixels = m_Lod0Pixels,
                Lod1Pixels = m_Lod1Pixels,
                Lod2Pixels = m_Lod2Pixels
            });
        }
    }
}
