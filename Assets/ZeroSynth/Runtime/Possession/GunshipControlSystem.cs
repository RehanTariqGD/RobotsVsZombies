using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace ZeroSynth
{
    /// <summary>
    /// Flies the possessed gunship, and sends it home otherwise.
    /// </summary>
    /// <remarks>
    /// Flight ignores the grid entirely: walls and terrain do not stop an
    /// aircraft. Fuel is the limit instead. When it runs out the system ends the
    /// possession itself by clearing <see cref="PossessionState"/>; the
    /// controller notices the state no longer names its entity and lets go.
    ///
    /// Weapons are gated by <see cref="Powered"/>, which is on only while the
    /// ship is being flown, so a parked or returning gunship does not shoot.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(SpatialGridBuildSystem))]
    public partial struct GunshipControlSystem : ISystem
    {
        const float ClimbSpeed = 9f;
        const float PadRadius = 1.5f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PossessionState>();
            state.RequireForUpdate<GunshipUnit>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var clock = SystemAPI.GetSingleton<SimClock>();
            if (clock.Paused)
                return;

            float dt = SystemAPI.Time.DeltaTime * math.max(0f, clock.Speed);

            var possessionRw = SystemAPI.GetSingletonRW<PossessionState>();
            var possession = possessionRw.ValueRO;

            // Powered and Possessed are disabled for a parked ship, so they are
            // matched as "present" and flipped through SystemAPI rather than
            // taken as EnabledRef parameters, which would require them enabled.
            foreach (var (shipRw, transformRw, entity) in
                     SystemAPI.Query<RefRW<GunshipUnit>, RefRW<LocalTransform>>()
                         .WithPresent<Powered, Possessed>()
                         .WithEntityAccess())
            {
                ref var ship = ref shipRw.ValueRW;
                ref var transform = ref transformRw.ValueRW;

                bool flown = possession.Mode == PossessionMode.Gunship && possession.Current == entity;

                if (flown)
                {
                    ship.Fuel = math.max(0f, ship.Fuel - ship.BurnPerSecond * dt);
                    if (ship.Fuel <= 0f)
                    {
                        // Out of fuel: end the sortie. The controller sees the
                        // cleared state and releases on its side.
                        possessionRw.ValueRW.Current = Entity.Null;
                        possessionRw.ValueRW.Mode = PossessionMode.None;
                        possessionRw.ValueRW.MoveInput = float2.zero;
                        possessionRw.ValueRW.HasAim = false;
                        possession = possessionRw.ValueRO;
                        SystemAPI.SetComponentEnabled<Possessed>(entity, false);
                        flown = false;
                    }
                }

                SystemAPI.SetComponentEnabled<Powered>(entity, flown);

                if (flown)
                    Fly(ref ship, ref transform, possession.MoveInput, dt);
                else
                    Return(ref ship, ref transform, dt);
            }
        }

        static void Fly(ref GunshipUnit ship, ref LocalTransform transform, float2 stick, float dt)
        {
            float magnitude = math.length(stick);
            float3 position = transform.Position;

            if (magnitude > 1e-3f)
            {
                float2 direction = stick / magnitude;
                float3 heading = new float3(direction.x, 0f, direction.y);

                transform.Rotation = math.slerp(
                    transform.Rotation,
                    quaternion.LookRotationSafe(heading, math.up()),
                    math.saturate(ship.TurnSpeed * dt));

                position += heading * ship.MoveSpeed * math.min(magnitude, 1f) * dt;
            }

            // Held inside its range of the pad rather than allowed to leave the map.
            float3 fromPad = position - ship.Pad;
            fromPad.y = 0f;
            float distance = math.length(fromPad);
            if (distance > ship.MaxRange)
            {
                float3 clamped = ship.Pad + fromPad / distance * ship.MaxRange;
                position.x = clamped.x;
                position.z = clamped.z;
            }

            position.y = math.min(ship.FlightHeight, position.y + ClimbSpeed * dt);
            transform.Position = position;
        }

        static void Return(ref GunshipUnit ship, ref LocalTransform transform, float dt)
        {
            float3 position = transform.Position;
            float3 toPad = ship.Pad - position;
            toPad.y = 0f;
            float distance = math.length(toPad);

            if (distance > PadRadius * 0.25f)
            {
                float3 heading = toPad / distance;
                transform.Rotation = math.slerp(
                    transform.Rotation,
                    quaternion.LookRotationSafe(heading, math.up()),
                    math.saturate(ship.TurnSpeed * dt));

                float step = math.min(distance, ship.MoveSpeed * 0.8f * dt);
                position.x += heading.x * step;
                position.z += heading.z * step;
            }

            // Descends only once it is over the pad.
            bool overPad = distance <= PadRadius;
            float targetY = overPad ? ship.ParkedHeight : math.max(position.y, ship.FlightHeight);
            position.y = math.lerp(position.y, targetY, math.saturate(2f * dt));
            transform.Position = position;

            if (overPad && position.y <= ship.ParkedHeight + 0.5f)
                ship.Fuel = math.min(ship.MaxFuel, ship.Fuel + ship.RefillPerSecond * dt);
        }
    }
}
