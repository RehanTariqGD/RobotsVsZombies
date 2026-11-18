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

        [Tooltip("Tap radius for the gunship, measured on the plane it flies in.")]
        [SerializeField, Min(0.5f)] float m_GunshipGrabRadius = 5f;

        [Tooltip("Ground plane height the pointer is projected onto.")]
        [SerializeField] float m_GroundHeight;

        [Tooltip("Drag distance in pixels for full stick deflection when driving a mech.")]
        [SerializeField, Min(20f)] float m_StickRange = 160f;

        EntityManager m_EntityManager;
        EntityQuery m_TurretQuery;
        EntityQuery m_MechQuery;
        EntityQuery m_GunshipQuery;
        Entity m_StateEntity;
        bool m_Ready;
        bool m_WasPressed;
        bool m_PressMoved;
        bool m_PressBlocked;
        float m_PressTime;
        Vector2 m_PressOrigin;

        /// <summary>Pixels a press may drift and still count as a tap.</summary>
        const float TapMoveTolerance = 24f;

        /// <summary>Longest press, in seconds, that still counts as a tap.</summary>
        const float TapMaxSeconds = 0.35f;

        /// <summary>
        /// Set false while a menu or overlay is up. This controller polls the
        /// pointer directly, so without the gate a tap on a menu button would
        /// also read as a tap on the ground behind it and could grab a turret.
        /// GameFlow owns this; it lives here because the Game assembly depends
        /// on Authoring, not the other way round.
        /// </summary>
        public static bool InputEnabled = true;

        /// <summary>
        /// The build menu has a structure selected, so a ground tap places it
        /// rather than possessing a turret.
        /// </summary>
        public static bool BuildModeActive;

        /// <summary>
        /// Reports whether a screen point is over UI that should swallow the
        /// pointer. Set by the Game assembly, which owns the UI documents.
        /// </summary>
        public static System.Func<Vector2, bool> PointerBlocker;

        /// <summary>What the player is currently driving, for UI hints. Static so UI need not find the instance.</summary>
        public static PossessionMode ActiveMode { get; private set; }

        /// <summary>Gunship fuel as 0..1, or a negative value when the scene has no gunship.</summary>
        public static float GunshipFuel01 { get; private set; } = -1f;

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

                // A destroyed turret is hidden but its entity stays, so without
                // this a tap on the empty spot would possess an invisible,
                // permanently unpowered weapon.
                None = new[] { ComponentType.ReadOnly<Unity.Rendering.DisableRendering>() },
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

            m_GunshipQuery = m_EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<GunshipUnit>(),
                    ComponentType.ReadOnly<LocalTransform>()
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState
            });

            GunshipFuel01 = -1f;
            ActiveMode = PossessionMode.None;

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
            if (!m_Ready) return;

            SyncWithSimulation();

            if (!InputEnabled || BuildModeActive) return;

            var pointer = Pointer.current;
            var keyboard = Keyboard.current;

            Vector2 screen = pointer != null ? pointer.position.ReadValue() : Vector2.zero;
            bool pressed = pointer != null && pointer.press.isPressed;
            bool justPressed = pressed && !m_WasPressed;
            bool justReleased = !pressed && m_WasPressed;
            m_WasPressed = pressed;

            if (justPressed)
            {
                m_PressOrigin = screen;
                m_PressTime = Time.unscaledTime;
                m_PressMoved = false;

                // A press that starts on a button or panel belongs to the UI:
                // it must not steer, aim or count as a tap on the world.
                m_PressBlocked = PointerBlocker != null && PointerBlocker(screen);
            }

            if (pressed && !m_PressMoved
                && (screen - m_PressOrigin).sqrMagnitude > TapMoveTolerance * TapMoveTolerance)
                m_PressMoved = true;

            // A tap is a quick press that never moved, decided on release. It
            // has to be: the same gesture starts a drag, and deciding on press
            // made every drag to steer the mech also read as a tap on it, which
            // released it again.
            if (justReleased && !m_PressMoved && !m_PressBlocked
                && Time.unscaledTime - m_PressTime <= TapMaxSeconds
                && !(PointerBlocker != null && PointerBlocker(screen)))
            {
                if (TryGetGroundPoint(screen, out float3 tapGround))
                    HandleTap(screen, tapGround);
            }

            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame && Possessed != Entity.Null)
                Release();

            if (Mode == PossessionMode.Turret && pressed && !m_PressBlocked)
            {
                // Aim only tracks while held; a touchscreen has no hover.
                if (TryGetGroundPoint(screen, out float3 aimGround))
                    SetAim(aimGround);
            }
            else if (Mode == PossessionMode.Mech || Mode == PossessionMode.Gunship)
            {
                // A drag from the press point acts as a virtual stick, in screen
                // space so it stays consistent while the follow camera moves
                // under it. Keys take over when held, for testing in the editor.
                float2 stick = pressed && m_PressMoved && !m_PressBlocked
                    ? (float2)((screen - m_PressOrigin) / m_StickRange)
                    : float2.zero;

                float2 keys = ReadMoveKeys(keyboard);
                if (math.lengthsq(keys) > 0f)
                    stick = keys;

                float magnitude = math.length(stick);
                if (magnitude > 1f) stick /= magnitude;

                SetMoveInput(stick);
            }
        }

        /// <summary>
        /// Lets the simulation end a possession (the gunship running dry) and
        /// publishes the gunship's fuel for the HUD.
        /// </summary>
        void SyncWithSimulation()
        {
            if (Possessed != Entity.Null)
            {
                bool gone = !m_EntityManager.Exists(Possessed);
                if (!gone)
                {
                    var state = m_EntityManager.GetComponentData<PossessionState>(m_StateEntity);
                    gone = state.Current != Possessed;
                }

                if (gone) Release();
            }

            if (m_GunshipQuery.CalculateEntityCount() == 1)
            {
                var ship = m_GunshipQuery.GetSingleton<GunshipUnit>();
                GunshipFuel01 = ship.MaxFuel > 0f ? Mathf.Clamp01(ship.Fuel / ship.MaxFuel) : 0f;
            }
            else
            {
                GunshipFuel01 = -1f;
            }
        }

        static float2 ReadMoveKeys(Keyboard keyboard)
        {
            if (keyboard == null) return float2.zero;

            float x = 0f, y = 0f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) y -= 1f;
            return new float2(x, y);
        }

        void HandleTap(Vector2 screen, float3 ground)
        {
            // The gunship is hit-tested where it actually is. Projecting a tap
            // on its image down to the ground lands well beyond it, because it
            // flies high. If it cannot launch the tap falls through, so a
            // turret under the same finger is still reachable.
            var ship = FindGunshipAt(screen);
            if (ship != Entity.Null)
            {
                if (ship == Possessed)
                {
                    Release();
                    return;
                }

                if (TryPossessGunship(ship))
                    return;
            }

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

        bool TryPossessGunship(Entity ship)
        {
            if (!m_EntityManager.Exists(ship)) return false;

            var data = m_EntityManager.GetComponentData<GunshipUnit>(ship);
            if (data.Fuel < data.MinLaunchFuel)
            {
                Debug.Log("[ZeroSynth] Gunship is refuelling. It needs more fuel to launch.");
                return false;
            }

            Possess(ship, PossessionMode.Gunship);
            return true;
        }

        Entity FindGunshipAt(Vector2 screen)
        {
            var entities = m_GunshipQuery.ToEntityArray(Allocator.Temp);
            var transforms = m_GunshipQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            Entity best = Entity.Null;
            float bestSq = m_GunshipGrabRadius * m_GunshipGrabRadius;

            for (int i = 0; i < entities.Length; i++)
            {
                if (!TryGetPlanePoint(screen, transforms[i].Position.y, out float3 hit))
                    continue;

                float3 delta = hit - transforms[i].Position;
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
            ActiveMode = mode;

            var state = m_EntityManager.GetComponentData<PossessionState>(m_StateEntity);
            state.Current = entity;
            state.Mode = mode;
            state.MoveInput = float2.zero;
            m_EntityManager.SetComponentData(m_StateEntity, state);

            Debug.Log(mode == PossessionMode.Mech
                ? "[ZeroSynth] Possessed mech. Drag to drive, tap it again to release."
                : mode == PossessionMode.Gunship
                    ? "[ZeroSynth] Flying gunship. Drag to fly, tap it again to return to the pad."
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
            ActiveMode = PossessionMode.None;

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
            return TryGetPlanePoint(screenPosition, m_GroundHeight, out result);
        }

        bool TryGetPlanePoint(Vector2 screenPosition, float height, out float3 result)
        {
            result = float3.zero;

            var ray = m_Camera.ScreenPointToRay(screenPosition);
            if (Mathf.Abs(ray.direction.y) < 1e-5f)
                return false;

            float t = (height - ray.origin.y) / ray.direction.y;
            if (t < 0f)
                return false;

            Vector3 hit = ray.origin + ray.direction * t;
            result = new float3(hit.x, height, hit.z);
            return true;
        }

        void OnDestroy()
        {
            if (!m_Ready) return;
            m_TurretQuery.Dispose();
            m_MechQuery.Dispose();
            m_GunshipQuery.Dispose();
            GunshipFuel01 = -1f;
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
