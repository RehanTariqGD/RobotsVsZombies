using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Random = Unity.Mathematics.Random;

namespace ZeroSynth
{
    /// <summary>
    /// Drives the prepare / spawn / fight cycle and releases agents from the
    /// pool.
    /// </summary>
    /// <remarks>
    /// Composition is derived from the run seed and the wave index, so a run
    /// replays identically — which is what lets the save in M8 persist a seed
    /// and a wave number instead of several thousand agents.
    ///
    /// Spawning never creates entities. It takes dormant ones from the pool
    /// and enables them, so a wave costs no structural changes at all. The
    /// pool is therefore a hard ceiling: a wave that wants more agents than
    /// remain simply gets fewer.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(AgentSteeringSystem))]
    public partial struct WaveDirectorSystem : ISystem
    {
        EntityQuery m_DormantQuery;
        EntityQuery m_ActiveQuery;

        public void OnCreate(ref SystemState state)
        {
            m_DormantQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<HordeAgent>()
                .WithDisabled<Active>()
                .Build(ref state);

            m_ActiveQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<HordeAgent, Active>()
                .Build(ref state);

            state.RequireForUpdate<WaveConfig>();
            state.RequireForUpdate<WaveState>();
            state.RequireForUpdate<SpawnProfile>();
            state.RequireForUpdate<HordeTarget>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var clock = SystemAPI.GetSingleton<SimClock>();
            if (clock.Paused)
                return;

            float dt = SystemAPI.Time.DeltaTime * math.max(0f, clock.Speed);

            var config = SystemAPI.GetSingleton<WaveConfig>();
            var profile = SystemAPI.GetSingleton<SpawnProfile>();
            var target = SystemAPI.GetSingleton<HordeTarget>();

            var waveRw = SystemAPI.GetSingletonRW<WaveState>();
            var wave = waveRw.ValueRO;

            switch (wave.Phase)
            {
                case WavePhase.Preparing:
                    wave.Timer -= dt;
                    if (wave.Timer <= 0f)
                        BeginWave(ref wave, config);
                    break;

                case WavePhase.Spawning:
                    wave.Timer += dt;
                    Release(ref state, ref wave, config, profile, target, dt);
                    if (wave.Remaining <= 0)
                        wave.Phase = WavePhase.Fighting;
                    break;

                case WavePhase.Fighting:
                    wave.Timer += dt;
                    if (m_ActiveQuery.IsEmpty)
                    {
                        var economy = SystemAPI.GetSingletonRW<Economy>();
                        economy.ValueRW.MetalScrap += config.ScrapPerWave;

                        wave.Phase = WavePhase.Preparing;
                        wave.Timer = config.PrepareDuration;
                    }
                    break;
            }

            waveRw.ValueRW = wave;
        }

        static void BeginWave(ref WaveState wave, WaveConfig config)
        {
            wave.Index++;
            wave.Phase = WavePhase.Spawning;
            wave.Timer = 0f;
            wave.Released = 0;
            wave.SpawnAccumulator = 0f;

            float growth = math.pow(1f + config.CountGrowth, math.max(0, wave.Index - 1));
            wave.Remaining = (int)math.round(config.BaseCount * growth);
        }

        void Release(
            ref SystemState state,
            ref WaveState wave,
            WaveConfig config,
            SpawnProfile profile,
            HordeTarget target,
            float dt)
        {
            wave.SpawnAccumulator += config.SpawnRate * dt;

            int budget = (int)wave.SpawnAccumulator;
            if (budget <= 0) return;

            wave.SpawnAccumulator -= budget;
            budget = math.min(budget, wave.Remaining);

            var dormant = m_DormantQuery.ToEntityArray(Allocator.Temp);
            int take = math.min(budget, dormant.Length);

            if (take > 0)
            {
                // Seeded on the wave index and the running release count, so
                // the same wave of the same run always produces the same
                // positions regardless of frame pacing.
                var random = Random.CreateFromIndex(
                    config.Seed ^ (uint)(wave.Index * 7919 + wave.Released));

                float speedScale = math.pow(1f + config.SpeedGrowth, math.max(0, wave.Index - 1));
                var em = state.EntityManager;

                for (int i = 0; i < take; i++)
                {
                    var entity = dormant[i];

                    float angle = random.NextFloat(0f, math.PI * 2f);
                    float radius = random.NextFloat(profile.InnerRadius, profile.OuterRadius);
                    float3 position = target.Position + new float3(
                        math.cos(angle) * radius, 0f, math.sin(angle) * radius);
                    position.y = 0f;

                    em.SetComponentData(entity, LocalTransform.FromPosition(position));
                    em.SetComponentData(entity, new HordeAgent
                    {
                        Speed = random.NextFloat(profile.MinSpeed, profile.MaxSpeed) * speedScale
                    });
                    em.SetComponentData(entity, new Health
                    {
                        Current = profile.MaxHealth,
                        Max = profile.MaxHealth
                    });
                    em.SetComponentEnabled<Active>(entity, true);
                }

                wave.Remaining -= take;
                wave.Released += take;
            }
            else if (dormant.Length == 0)
            {
                // Pool exhausted. Drop the rest of the wave rather than
                // stalling in Spawning forever waiting for corpses.
                wave.Remaining = 0;
            }

            dormant.Dispose();
        }
    }
}
