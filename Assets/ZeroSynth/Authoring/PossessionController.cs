using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ZeroSynth.Authoring
{
    /// <summary>
    /// Touch and mouse control for turret possession.
    /// </summary>
    /// <remarks>
    /// Tap a turret to take it, drag to sweep its fire, tap it again to
    /// release. The tactical camera does not move — a turret does not need a
    /// first-person view to be aimed, and keeping the fixed isometric angle
    /// preserves the culling the frame budget depends on (rule R5). The
    /// ground-level camera work belongs with the mech and the gunship, where
    /// it also needs its own quality profile.
    /// </remarks>
    public sealed class PossessionController : MonoBehaviour
    {
        [SerializeField] Camera m_Camera;

        [Tooltip("How close a tap must land to a turret to take it, in world units.")]
        [SerializeField, Min(0.5f)] float m_GrabRadius = 6f;

        [Tooltip("Ground plane height the pointer is projected onto.")]
        [SerializeField] float m_GroundHeight;

        EntityManager m_EntityManager;
        EntityQuery m_TurretQuery;
        Entity m_StateEntity;
        bool m_Ready;
        bool m_WasPressed;

        public Entity Possessed { get; private set; }

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            if (m_Camera == null) m_Camera = Camera.main;
            if (m_Camera == null)
            {
                Debug.LogError("[ZeroSynth] PossessionController needs a camera.", this);
                return;
            }

            m_EntityManager = world.EntityManager;

            m_TurretQuery = m_EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Turret>(),
                    ComponentType.ReadOnly<LocalTransform>()
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState
            });

            m_StateEntity = m_EntityManager.CreateEntity();
            m_EntityManager.SetName(m_StateEntity, "PossessionState");
            m_EntityManager.AddComponentData(m_StateEntity, new PossessionState
            {
                Current = Entity.Null,
                AimPoint = float3.zero,
                HasAim = false
            });

            Possessed = Entity.Null;
            m_Ready = true;
        }

        void Update()
        {
            if (!m_Ready) return;

            var pointer = Pointer.current;
            if (pointer == null) return;

            bool pressed = pointer.press.isPressed;
            bool justPressed = pressed && !m_WasPressed;
            m_WasPressed = pressed;

            if (!TryGetGroundPoint(pointer.position.ReadValue(), out float3 ground))
                return;

            if (justPressed)
                HandleTap(ground);

            // Aim only tracks while held, since a touchscreen has no hover.
            if (pressed && Possessed != Entity.Null)
                SetAim(ground);
        }

        void HandleTap(float3 ground)
        {
            var nearest = FindTurretNear(ground, m_GrabRadius);

            if (nearest == Entity.Null)
                return;

            if (nearest == Possessed)
            {
                Release();
                return;
            }

            Possess(nearest);
        }

        void Possess(Entity turret)
        {
            if (Possessed != Entity.Null)
                Release();

            if (!m_EntityManager.Exists(turret)) return;

            m_EntityManager.SetComponentEnabled<ZeroSynth.Possessed>(turret, true);
            Possessed = turret;

            var state = m_EntityManager.GetComponentData<PossessionState>(m_StateEntity);
            state.Current = turret;
            m_EntityManager.SetComponentData(m_StateEntity, state);

            Debug.Log("[ZeroSynth] Possessed turret. Drag to aim, tap it again to release.");
        }

        public void Release()
        {
            if (Possessed != Entity.Null && m_EntityManager.Exists(Possessed))
                m_EntityManager.SetComponentEnabled<ZeroSynth.Possessed>(Possessed, false);

            Possessed = Entity.Null;

            var state = m_EntityManager.GetComponentData<PossessionState>(m_StateEntity);
            state.Current = Entity.Null;
            state.HasAim = false;
            m_EntityManager.SetComponentData(m_StateEntity, state);
        }

        void SetAim(float3 ground)
        {
            var state = m_EntityManager.GetComponentData<PossessionState>(m_StateEntity);
            state.AimPoint = ground;
            state.HasAim = true;
            m_EntityManager.SetComponentData(m_StateEntity, state);
        }

        Entity FindTurretNear(float3 point, float radius)
        {
            var entities = m_TurretQuery.ToEntityArray(Allocator.Temp);
            var transforms = m_TurretQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            Entity best = Entity.Null;
            float bestSq = radius * radius;

            for (int i = 0; i < entities.Length; i++)
            {
                float3 delta = transforms[i].Position - point;
                delta.y = 0f;

                float d = math.lengthsq(delta);
                if (d <= bestSq)
                {
                    bestSq = d;
                    best = entities[i];
                }
            }

            entities.Dispose();
            transforms.Dispose();
            return best;
        }

        /// <summary>
        /// Projects the pointer onto the ground plane. A plane intersection
        /// rather than a physics raycast: there are no colliders in the scene,
        /// and the ground is flat by design.
        /// </summary>
        bool TryGetGroundPoint(Vector2 screenPosition, out float3 result)
        {
            result = float3.zero;

            var ray = m_Camera.ScreenPointToRay(screenPosition);
            if (Mathf.Abs(ray.direction.y) < 1e-5f)
                return false;

            float t = (m_GroundHeight - ray.origin.y) / ray.direction.y;
            if (t < 0f)
                return false;

            Vector3 hit = ray.origin + ray.direction * t;
            result = new float3(hit.x, m_GroundHeight, hit.z);
            return true;
        }

        void OnDestroy()
        {
            if (m_Ready) m_TurretQuery.Dispose();
        }

        void OnDrawGizmos()
        {
            if (!Application.isPlaying || !m_Ready) return;
            if (Possessed == Entity.Null || !m_EntityManager.Exists(Possessed)) return;

            var state = m_EntityManager.GetComponentData<PossessionState>(m_StateEntity);
            if (!state.HasAim) return;

            Gizmos.color = new Color(1f, 0.35f, 0.25f, 0.9f);
            Gizmos.DrawWireSphere(state.AimPoint, 2f);
        }
    }
}
