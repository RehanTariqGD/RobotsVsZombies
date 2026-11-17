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

        [Tooltip("Drag distance in pixels for full stick deflection when driving a mech.")]
        [SerializeField, Min(20f)] float m_StickRange = 160f;

        EntityManager m_EntityManager;
        EntityQuery m_TurretQuery;
        EntityQuery m_MechQuery;
        Entity m_StateEntity;
        bool m_Ready;
        bool m_WasPressed;
        Vector2 m_PressOrigin;

        /// <summary>
        /// Set false while a menu or overlay is up. This controller polls the
        /// pointer directly, so without the gate a tap on a menu button would
        /// also read as a tap on the ground behind it and could grab a turret.
        /// GameFlow owns this; it lives here because the Game assembly depends
        /// on Authoring, not the other way round.
        /// </summary>
        public static bool InputEnabled = true;

        public Entity Possessed { get; private set; }
        public PossessionMode Mode { get; private set; }

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

            m_MechQuery = m_EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<MechUnit>(),
                    ComponentType.ReadOnly<LocalTransform>()
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState
            });

            m_StateEntity = m_EntityManager.CreateEntity();
            m_EntityManager.SetName(m_StateEntity, "PossessionState");
            m_EntityManager.AddComponentData(m_StateEntity, new PossessionState
            {
                Current = Entity.Null,
                Mode = PossessionMode.None,
                AimPoint = float3.zero,
                HasAim = false,
                MoveInput = float2.zero
            });

            Possessed = Entity.Null;
            Mode = PossessionMode.None;
            m_Ready = true;
        }

        void Update()
        {
            if (!m_Ready || !InputEnabled) return;

            var pointer = Pointer.current;
            if (pointer == null) return;

            Vector2 screen = pointer.position.ReadValue();
            bool pressed = pointer.press.isPressed;
            bool justPressed = pressed && !m_WasPressed;
            bool justReleased = !pressed && m_WasPressed;
            m_WasPressed = pressed;

            if (justPressed)
            {
                m_PressOrigin = screen;
                if (TryGetGroundPoint(screen, out float3 tapGround))
                    HandleTap(tapGround);
            }

            if (Mode == PossessionMode.Turret && pressed)
            {
                // Aim only tracks while held; a touchscreen has no hover.
                if (TryGetGroundPoint(screen, out float3 aimGround))
                    SetAim(aimGround);
            }
            else if (Mode == PossessionMode.Mech)
            {
                // Drag from the press point acts as a virtual stick. Screen
                // space, not world space, so the stick stays consistent while
                // the follow camera moves under it.
                float2 stick = pressed
                    ? (float2)((screen - m_PressOrigin) / m_StickRange)
                    : float2.zero;

                float magnitude = math.length(stick);
                if (magnitude > 1f) stick /= magnitude;

                SetMoveInput(stick);
            }

            if (justReleased && Mode == PossessionMode.Mech)
                SetMoveInput(float2.zero);
        }

        void HandleTap(float3 ground)
        {
            var mech = FindNear(m_MechQuery, ground, m_GrabRadius);
            if (mech != Entity.Null)
            {
                if (mech == Possessed) Release();
                else Possess(mech, PossessionMode.Mech);
                return;
            }

            var turret = FindNear(m_TurretQuery, ground, m_GrabRadius);
            if (turret == Entity.Null)
                return;

            if (turret == Possessed) Release();
            else Possess(turret, PossessionMode.Turret);
        }

        void Possess(Entity entity, PossessionMode mode)
        {
            if (Possessed != Entity.Null)
                Release();

            if (!m_EntityManager.Exists(entity)) return;

            m_EntityManager.SetComponentEnabled<ZeroSynth.Possessed>(entity, true);

            // Only a hand-aimed weapon suppresses auto-targeting. A driven
            // mech keeps shooting for itself while the player steers.
            if (mode == PossessionMode.Turret
                && m_EntityManager.HasComponent<ManualAim>(entity))
            {
                m_EntityManager.SetComponentEnabled<ManualAim>(entity, true);
            }

            Possessed = entity;
            Mode = mode;

            var state = m_EntityManager.GetComponentData<PossessionState>(m_StateEntity);
            state.Current = entity;
            state.Mode = mode;
            state.MoveInput = float2.zero;
            m_EntityManager.SetComponentData(m_StateEntity, state);

            Debug.Log(mode == PossessionMode.Mech
                ? "[ZeroSynth] Possessed mech. Drag to drive, tap it again to release."
                : "[ZeroSynth] Possessed turret. Drag to aim, tap it again to release.");
        }

        public void Release()
        {
            if (Possessed != Entity.Null && m_EntityManager.Exists(Possessed))
            {
                m_EntityManager.SetComponentEnabled<ZeroSynth.Possessed>(Possessed, false);
                if (m_EntityManager.HasComponent<ManualAim>(Possessed))
                    m_EntityManager.SetComponentEnabled<ManualAim>(Possessed, false);
            }

            Possessed = Entity.Null;
            Mode = PossessionMode.None;

            var state = m_EntityManager.GetComponentData<PossessionState>(m_StateEntity);
            state.Current = Entity.Null;
            state.Mode = PossessionMode.None;
            state.HasAim = false;
            state.MoveInput = float2.zero;
            m_EntityManager.SetComponentData(m_StateEntity, state);
        }

        void SetAim(float3 ground)
        {
            var state = m_EntityManager.GetComponentData<PossessionState>(m_StateEntity);
            state.AimPoint = ground;
            state.HasAim = true;
            m_EntityManager.SetComponentData(m_StateEntity, state);
        }

        void SetMoveInput(float2 stick)
        {
            var state = m_EntityManager.GetComponentData<PossessionState>(m_StateEntity);
            state.MoveInput = stick;
            m_EntityManager.SetComponentData(m_StateEntity, state);
        }

        static Entity FindNear(EntityQuery query, float3 point, float radius)
        {
            var entities = query.ToEntityArray(Allocator.Temp);
            var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

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
            if (!m_Ready) return;
            m_TurretQuery.Dispose();
            m_MechQuery.Dispose();
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
