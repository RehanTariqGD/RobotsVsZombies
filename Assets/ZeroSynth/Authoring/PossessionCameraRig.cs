using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace ZeroSynth.Authoring
{
    /// <summary>
    /// Moves the camera into a follow position while the mech is being driven,
    /// and applies that mode's quality profile.
    /// </summary>
    /// <remarks>
    /// Every frame budget in the technical review assumes the tactical
    /// camera's bounded view volume. A mech camera sits lower and sees a long
    /// corridor of horde receding to the horizon, which is both the moment the
    /// game most wants to look good and the moment the renderer has least to
    /// give. Each mode therefore carries its own profile — a tighter far plane
    /// justified in fiction by sensor range, a shorter shadow distance, and a
    /// heavier LOD bias.
    ///
    /// The LOD bias has little to act on yet: the four-tier horde LOD ladder
    /// from the technical review is not built, so agents are still a single
    /// mesh. The hook is here so the ladder lands into a profile that already
    /// exists rather than needing this retrofitted around it.
    /// </remarks>
    public sealed class PossessionCameraRig : MonoBehaviour
    {
        [System.Serializable]
        public struct Profile
        {
            [Min(10f)] public float FarClip;
            [Min(0f)] public float ShadowDistance;

            [Tooltip("Lower values drop to cheaper LODs sooner.")]
            [Min(0.1f)] public float LodBias;
        }

        [SerializeField] Camera m_Camera;

        [Header("Tactical (default)")]
        [SerializeField] Profile m_TacticalProfile = new Profile
        {
            FarClip = 400f,
            ShadowDistance = 50f,
            LodBias = 1f
        };

        [Header("Mech follow")]
        [Tooltip("Offset from the mech, in world space. Lower and closer than tactical.")]
        [SerializeField] Vector3 m_MechOffset = new Vector3(0f, 18f, -22f);

        [SerializeField, Min(0.5f)] float m_MechPitch = 35f;
        [SerializeField, Min(0.1f)] float m_FollowLerp = 6f;

        [Tooltip("Seconds the camera stays close after the stick is released.")]
        [SerializeField, Min(0f)] float m_IdleSeconds = 1.2f;

        [SerializeField] Profile m_MechProfile = new Profile
        {
            FarClip = 120f,
            ShadowDistance = 25f,
            LodBias = 0.6f
        };

        EntityManager m_EntityManager;
        EntityQuery m_StateQuery;
        bool m_Ready;

        Vector3 m_TacticalPosition;
        Quaternion m_TacticalRotation;
        PossessionMode m_AppliedMode = PossessionMode.None;
        float m_LastDriveTime = float.NegativeInfinity;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            if (m_Camera == null) m_Camera = Camera.main;
            if (m_Camera == null) return;

            m_EntityManager = world.EntityManager;
            m_StateQuery = m_EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PossessionState>());

            m_TacticalPosition = m_Camera.transform.position;
            m_TacticalRotation = m_Camera.transform.rotation;

            Apply(m_TacticalProfile);
            m_AppliedMode = PossessionMode.None;
            m_Ready = true;
        }

        void LateUpdate()
        {
            if (!m_Ready || m_StateQuery.IsEmpty) return;

            var state = m_EntityManager.GetComponentData<PossessionState>(
                m_StateQuery.GetSingletonEntity());

            bool mech = state.Mode == PossessionMode.Mech
                        && state.Current != Entity.Null
                        && m_EntityManager.Exists(state.Current)
                        && m_EntityManager.HasComponent<LocalTransform>(state.Current);

            // The camera only drops in while the mech is actually being driven.
            // Possessing it alone keeps the overview, so clicking the mech does
            // not lurch the view. It stays close for a moment after the stick is
            // released, otherwise stopping to turn would bounce the camera in
            // and out.
            if (mech && math.lengthsq(state.MoveInput) > 0.0025f)
                m_LastDriveTime = Time.unscaledTime;

            bool driving = mech && Time.unscaledTime - m_LastDriveTime < m_IdleSeconds;

            var wanted = driving ? PossessionMode.Mech : PossessionMode.None;
            if (wanted != m_AppliedMode)
            {
                Apply(driving ? m_MechProfile : m_TacticalProfile);
                m_AppliedMode = wanted;
            }

            if (driving)
            {
                float3 pivot = m_EntityManager.GetComponentData<LocalTransform>(state.Current).Position;

                Vector3 target = (Vector3)pivot + m_MechOffset;
                var rotation = Quaternion.Euler(m_MechPitch, 0f, 0f);

                float t = 1f - Mathf.Exp(-m_FollowLerp * Time.deltaTime);
                m_Camera.transform.position = Vector3.Lerp(m_Camera.transform.position, target, t);
                m_Camera.transform.rotation = Quaternion.Slerp(m_Camera.transform.rotation, rotation, t);
            }
            else
            {
                float t = 1f - Mathf.Exp(-m_FollowLerp * Time.deltaTime);
                m_Camera.transform.position = Vector3.Lerp(m_Camera.transform.position, m_TacticalPosition, t);
                m_Camera.transform.rotation = Quaternion.Slerp(m_Camera.transform.rotation, m_TacticalRotation, t);
            }
        }

        void Apply(Profile profile)
        {
            m_Camera.farClipPlane = profile.FarClip;
            QualitySettings.shadowDistance = profile.ShadowDistance;
            QualitySettings.lodBias = profile.LodBias;
        }

        void OnDestroy()
        {
            if (!m_Ready) return;
            m_StateQuery.Dispose();

            // Leaving a reduced bias set would silently degrade every scene
            // loaded afterwards in the same editor session.
            QualitySettings.shadowDistance = m_TacticalProfile.ShadowDistance;
            QualitySettings.lodBias = m_TacticalProfile.LodBias;
        }
    }
}
