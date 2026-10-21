using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace ZeroSynth
{
    /// <summary>
    /// Drives the possessed mech from the virtual stick.
    /// </summary>
    /// <remarks>
    /// Only the possessed mech moves. An unpossessed one stands where it is,
    /// which is the whole design: the Goliath is a hole-plugger the player
    /// walks into a breach, not an autonomous unit.
    ///
    /// Walls block it exactly as they block agents — a mech cannot walk
    /// through its own barricades.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(SpatialGridBuildSystem))]
    public partial struct MechControlSystem : ISystem
    {
        EntityQuery m_GridQuery;

        public void OnCreate(ref SystemState state)
        {
            m_GridQuery = state.GetEntityQuery(ComponentType.ReadOnly<GridData>());
            state.RequireForUpdate<PossessionState>();
            state.RequireForUpdate<MechUnit>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var possession = SystemAPI.GetSingleton<PossessionState>();
            if (possession.Mode != PossessionMode.Mech || possession.Current == Entity.Null)
                return;

            if (!SystemAPI.HasComponent<MechUnit>(possession.Current))
                return;

            var clock = SystemAPI.GetSingleton<SimClock>();
            if (clock.Paused)
                return;

            float dt = SystemAPI.Time.DeltaTime * math.max(0f, clock.Speed);

            var mech = SystemAPI.GetComponent<MechUnit>(possession.Current);
            var transform = SystemAPI.GetComponent<LocalTransform>(possession.Current);

            float2 stick = possession.MoveInput;
            float magnitude = math.length(stick);

            if (magnitude > 1e-3f)
            {
                float2 direction = stick / magnitude;
                float3 heading = new float3(direction.x, 0f, direction.y);

                // Turn toward the stick rather than snapping. A 20-metre
                // walker that pivots instantly reads as weightless.
                transform.Rotation = math.slerp(
                    transform.Rotation,
                    quaternion.LookRotationSafe(heading, math.up()),
                    math.saturate(mech.TurnSpeed * dt));

                float3 next = transform.Position + heading * mech.MoveSpeed * math.min(magnitude, 1f) * dt;

                if (!Blocked(ref state, next))
                    transform.Position = next;
            }

            SystemAPI.SetComponent(possession.Current, transform);
        }

        bool Blocked(ref SystemState state, float3 position)
        {
            if (m_GridQuery.IsEmpty || !SystemAPI.HasSingleton<GridConfig>())
                return false;

            var config = SystemAPI.GetSingleton<GridConfig>();
            int2 cell = config.WorldToCell(position);
            if (!config.InBounds(cell))
                return true;

            var grid = state.EntityManager.GetComponentObject<GridData>(
                m_GridQuery.GetSingletonEntity());

            var flags = grid.Flags[config.Index(cell)];
            return (flags & (CellFlags.Wall | CellFlags.Impassable)) != 0;
        }
    }
}
